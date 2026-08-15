using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Market;

/// <summary>
/// HTTP, deserialisation, the <c>core.items</c> join, mapping and structural validation
/// (S2 5 / S4 7.4).
/// </summary>
/// <remarks>
/// <para>
/// It does not do context validation (D8-c / D8-e), commit judgement, epoch management or the
/// <c>Suspicious → LeagueUnresolved</c> transition; those are Polling's.
/// </para>
/// <para>
/// Failures are return values. The two public methods are the category and league entry points of
/// D-MK4, so each wraps everything in a boundary <c>catch (Exception)</c> that yields
/// <c>Fail(MappingFault)</c> — <c>required</c> does not stop a JSON <c>null</c>, and the resulting
/// <see cref="NullReferenceException"/> is not a <c>JsonException</c>, so without this catch it
/// escapes Market entirely and lands on the UI thread. Cancellation is control flow and is
/// rethrown untouched.
/// </para>
/// </remarks>
public sealed partial class MarketClient : IMarketClient
{
    /// <summary>S4 15.3 — three retries after the first attempt.</summary>
    internal const int MaxRetries = 3;

    /// <summary>S4 15.3 — per-attempt HTTP timeout.</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>S4 15.3 — total budget for one logical request, admission wait included.</summary>
    internal static readonly TimeSpan LogicalRequestTimeout = TimeSpan.FromSeconds(90);

    /// <summary>S4 15.3 — exponential backoff base.</summary>
    internal static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NinjaGateway _gateway;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarketClient> _logger;
    private readonly SessionSuppressionRegistry _suppression;

    /// <summary>
    /// Creates the client.
    /// </summary>
    /// <param name="suppression">
    /// The once-per-session channels of S2 5.1 ("state: the gateway and a suppression channel
    /// reference"). S4 7.4's constructor list omits it; it is optional here so that the documented
    /// four-argument form still compiles, and a private registry is created when it is absent.
    /// </param>
    public MarketClient(
        IHttpClientFactory httpClientFactory,
        NinjaGateway gateway,
        TimeProvider timeProvider,
        ILogger<MarketClient> logger,
        SessionSuppressionRegistry? suppression = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _gateway = gateway;
        _timeProvider = timeProvider;
        _logger = logger;
        _suppression = suppression ?? new SessionSuppressionRegistry(logger);
    }

    /// <summary>
    /// How many times a join dictionary has been built (E1, S4 7.4).
    /// </summary>
    /// <remarks>
    /// Test-only counter. Without it "build the dictionary once per response" has no observable
    /// surface at all and its regression test passes vacuously; a per-item rebuild makes this
    /// value grow, and a linear scan makes it stop growing.
    /// </remarks>
    internal int JoinDictionaryBuildCount { get; private set; }

    /// <inheritdoc />
    public async Task<MarketResult<CategorySnapshot>> FetchCategoryAsync(
        string league,
        ExchangeCategory category,
        RequestPriority priority,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(league);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Module"] = "Market",
            ["League"] = league,
            ["Category"] = category.ToString(),
        });

#pragma warning disable CA1031 // S2 9.5 row 3 (D-MK4): the observable result is an Error entry plus Fail(MappingFault).
        try
        {
            var transport = await SendAsync(NinjaEndpoints.OverviewUrl(league, category), priority, ct)
                .ConfigureAwait(false);

            if (transport is MarketResult<string>.Fail failed)
            {
                return new MarketResult<CategorySnapshot>.Fail(failed.Why);
            }

            var body = ((MarketResult<string>.Ok)transport).Value;
            return MapCategory(body, category, league);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, "MappingFault", "Unexpected exception while reading a category response.", ex);
            return new MarketResult<CategorySnapshot>.Fail(
                new FailureRecord(FailureKind.MappingFault, "MappingFault", _timeProvider.GetUtcNow(), null, null, ex.GetType().Name));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<MarketResult<LeagueList>> FetchLeaguesAsync(RequestPriority priority, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Module"] = "Market",
        });

#pragma warning disable CA1031 // S2 9.5 row 3 (D-MK4): the observable result is an Error entry plus Fail(MappingFault).
        try
        {
            var transport = await SendAsync(NinjaEndpoints.LeaguesUrl, priority, ct).ConfigureAwait(false);

            if (transport is MarketResult<string>.Fail failed)
            {
                // S2 5.9: a transport failure is still a verdict on the list, carrying the S4 13.3
                // code. Fail is reserved for the boundary catch below.
                return new MarketResult<LeagueList>.Ok(new LeagueList(
                    [],
                    _timeProvider.GetUtcNow(),
                    LeagueListStatus.Failed,
                    failed.Why.Code));
            }

            var body = ((MarketResult<string>.Ok)transport).Value;
            return MapLeagues(body);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, "MappingFault", "Unexpected exception while reading the league list.", ex);
            return new MarketResult<LeagueList>.Fail(
                new FailureRecord(FailureKind.MappingFault, "MappingFault", _timeProvider.GetUtcNow(), null, null, ex.GetType().Name));
        }
#pragma warning restore CA1031
    }

    private static bool IsRetriableStatus(HttpStatusCode status)
        => status == HttpStatusCode.RequestTimeout
            || status == HttpStatusCode.TooManyRequests
            || (int)status >= 500;

    private static string? RetryAfterHeader(HttpResponseMessage response)
        => response.Headers.TryGetValues("Retry-After", out var values)
            ? values.FirstOrDefault()
            : null;

    private static TimeSpan Backoff(int attempt)
    {
        var exponential = BackoffBase * Math.Pow(2, attempt);

        // Jitter on, so that eighteen categories failing together do not retry in lockstep.
        var jitter = Random.Shared.NextDouble() * 0.5;
        return exponential * (0.75 + jitter);
    }

    private async Task<MarketResult<string>> SendAsync(string url, RequestPriority priority, CancellationToken ct)
    {
        using var budget = new CancellationTokenSource(LogicalRequestTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);

        var http = _httpClientFactory.CreateClient(NinjaEndpoints.HttpClientName);

        try
        {
            using var response = await _gateway
                .SendAsync(token => SendWithRetriesAsync(http, url, token), priority, linked.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Log(LogLevel.Warning, "RateLimited", "poe.ninja is rate limiting; retries exhausted.");
                    return Fail(FailureKind.RateLimited, "RateLimited", status, null, null);
                }

                Log(LogLevel.Warning, "HttpStatus", FormattableString.Invariant($"poe.ninja returned HTTP {status}."));
                return Fail(FailureKind.HttpStatus, "HttpStatus", status, null, null);
            }

            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return new MarketResult<string>.Ok(body);
        }
        catch (TimeoutException ex)
        {
            Log(LogLevel.Warning, "Timeout", "Attempt timeout exhausted the retry budget.", ex);
            return Fail(FailureKind.Timeout, "Timeout", null, null, ex.GetType().Name);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The 90 s logical budget, not the caller. Caller cancellation propagates untouched.
            Log(LogLevel.Warning, "Timeout", "The logical request budget expired.", ex);
            return Fail(FailureKind.Timeout, "Timeout", null, null, ex.GetType().Name);
        }
        catch (HttpRequestException ex)
        {
            Log(LogLevel.Warning, "Network", "Could not reach poe.ninja.", ex);
            return Fail(FailureKind.Network, "Network", null, null, ex.GetType().Name);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetriesAsync(HttpClient http, string url, CancellationToken ct)
    {
        // One logical request holds one slot; the retries live inside it. Re-acquiring per attempt
        // would make a slot holder queue for a slot.
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                using var attemptBudget = new CancellationTokenSource(AttemptTimeout, _timeProvider);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, attemptBudget.Token);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                transportFailure = new TimeoutException("The per-attempt HTTP timeout elapsed.");
            }
            catch (HttpRequestException ex)
            {
                transportFailure = ex;
            }

            if (transportFailure is null && !IsRetriableStatus(response!.StatusCode))
            {
                return response;
            }

            if (attempt >= MaxRetries)
            {
                if (transportFailure is not null)
                {
                    throw transportFailure;
                }

                return response!;
            }

            var backoff = Backoff(attempt);
            var wait = transportFailure is null
                ? RetryAfterPolicy.Wait(RetryAfterPolicy.HeaderDelay(RetryAfterHeader(response!), _timeProvider.GetUtcNow()), backoff)
                : backoff;

            response?.Dispose();
            await Task.Delay(wait, _timeProvider, ct).ConfigureAwait(false);
        }
    }

    private MarketResult<string> Fail(
        FailureKind kind,
        string code,
        int? httpStatus,
        string? detail,
        string? exceptionType)
        => new MarketResult<string>.Fail(
            new FailureRecord(kind, code, _timeProvider.GetUtcNow(), httpStatus, detail, exceptionType));

    private void Log(LogLevel level, string code, string message, Exception? exception = null)
        => _logger.Log(level, new EventId(0, code), message, exception, static (state, _) => state);

    private void LogOnce(LogLevel level, string channel, string suppressionKey, string code, string message)
    {
        if (_suppression.ShouldReport(channel, suppressionKey))
        {
            Log(level, code, message);
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
