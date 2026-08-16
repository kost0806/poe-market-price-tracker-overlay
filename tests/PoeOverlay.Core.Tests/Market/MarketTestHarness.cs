using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Tests.TestSupport;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// Shared plumbing for the Market tests: an <c>HttpMessageHandler</c> stub, a client factory over
/// it and the fixed assets measured in 00-api-contract.md (S4 16.8).
/// </summary>
internal static class MarketTestHarness
{
    internal static readonly DateTimeOffset Start = new(2026, 8, 16, 6, 0, 0, TimeSpan.Zero);

    /// <summary>Reads one measured body from the copied fixture assets.</summary>
    internal static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Market", "Fixtures", name));

    internal static MarketClient CreateClient(
        StubHandler handler,
        out FakeTimeProvider time,
        out RecordingLogger<MarketClient> logger)
    {
        time = new FakeTimeProvider(Start);
        logger = new RecordingLogger<MarketClient>();
        return new MarketClient(new StubHttpClientFactory(handler), new NinjaGateway(time), time, logger);
    }

    internal static MarketClient CreateClient(StubHandler handler, out FakeTimeProvider time)
        => CreateClient(handler, out time, out _);

    internal static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Drives a fake clock forward until <paramref name="task"/> finishes.
    /// </summary>
    /// <remarks>
    /// Every wait in Market — the gateway's 250 ms floor, the retry backoff, the per-attempt and
    /// logical timeouts — is driven by <c>TimeProvider</c>, so a test never sleeps for real.
    /// </remarks>
    internal static async Task<T> RunAsync<T>(FakeTimeProvider time, Task<T> task, int maxSteps = 400)
    {
        for (var step = 0; step < maxSteps && !task.IsCompleted; step++)
        {
            // A one-millisecond real wait parks the test thread so the pool can run the
            // continuation; it drives nothing, since every wait under test is on the fake clock.
            await Task.Delay(1).ConfigureAwait(false);
            if (!task.IsCompleted)
            {
                time.Advance(TimeSpan.FromMilliseconds(250));
            }
        }

        Assert.True(task.IsCompleted, "The operation never completed on the fake clock.");
        return await task.ConfigureAwait(false);
    }

    /// <summary>
    /// The rate basis measured in every one of the 18 categories: exactly two entries, and never
    /// the name table (00-api-contract.md 2.0).
    /// </summary>
    internal const string RateBasisItems =
        """{"id":"chaos","name":"Chaos Orb","category":"Currency","detailsId":"chaos-orb"},""" +
        """{"id":"divine","name":"Divine Orb","category":"Currency","detailsId":"divine-orb"}""";

    /// <summary>
    /// Builds an overview body with <paramref name="count"/> generated lines, in the measured
    /// three-key shape: <c>core</c> (rate basis), <c>lines</c>, and the root <c>items</c> name table.
    /// </summary>
    /// <param name="itemCount">
    /// How many entries the <em>name table</em> carries; the rate basis is always the measured two.
    /// </param>
    internal static string Overview(int count, Func<int, string> line, string primary = "chaos", int itemCount = -1)
    {
        var items = new StringBuilder();
        var effectiveItems = itemCount < 0 ? count : itemCount;
        for (var i = 0; i < effectiveItems; i++)
        {
            if (i > 0)
            {
                items.Append(',');
            }

            items.Append(CultureInfo.InvariantCulture, $$"""
                {"id":"item-{{i}}","name":"Item {{i}}","image":"/x.png","category":"Currency","detailsId":"item-{{i}}"}
                """);
        }

        var lines = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                lines.Append(',');
            }

            lines.Append(line(i));
        }

        return $$"""
            {"core":{"primary":"{{primary}}","secondary":"divine","items":[{{RateBasisItems}}]},
             "lines":[{{lines}}],
             "items":[{{items}}]}
            """;
    }

    /// <summary>A well-formed line for item <paramref name="index"/>.</summary>
    internal static string GoodLine(int index)
        => string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"id":"item-{{{index}}}","primaryValue":{{{index + 1}}},"volumePrimaryValue":10,"maxVolumeCurrency":"chaos","maxVolumeRate":1,"sparkline":{"totalChange":1.5,"data":[1,2]}}""");
}

/// <summary>An <c>HttpMessageHandler</c> that answers from a delegate and records what it saw.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;
    private readonly TimeProvider? _timeProvider;
    private readonly List<string> _urls = [];
    private readonly List<DateTimeOffset> _issuedAt = [];
    private int _calls;

    internal StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder, TimeProvider? timeProvider = null)
    {
        _responder = responder;
        _timeProvider = timeProvider;
    }

    internal StubHandler(string body)
        : this((_, _) => MarketTestHarness.Json(body))
    {
    }

    internal int Calls => Volatile.Read(ref _calls);

    internal IReadOnlyList<string> Urls
    {
        get
        {
            lock (_urls)
            {
                return _urls.ToArray();
            }
        }
    }

    internal IReadOnlyList<DateTimeOffset> IssuedAt
    {
        get
        {
            lock (_urls)
            {
                return _issuedAt.ToArray();
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var index = Interlocked.Increment(ref _calls) - 1;
        lock (_urls)
        {
            _urls.Add(request.RequestUri?.ToString() ?? string.Empty);
            if (_timeProvider is not null)
            {
                _issuedAt.Add(_timeProvider.GetUtcNow());
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_responder(request, index));
    }
}

/// <summary>Hands out clients over one stub handler.</summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    internal StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

/// <summary>A factory that fails, injecting an unexpected exception into the entry point (M23).</summary>
internal sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => throw new InvalidOperationException("Injected fault.");
}
