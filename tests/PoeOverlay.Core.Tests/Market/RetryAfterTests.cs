using System.Globalization;
using System.Net;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M16–M18 — <c>Retry-After</c> handling (S2 5.8). The server's instruction is a floor
/// under our own backoff, clamped to sixty seconds.
/// </summary>
public sealed class RetryAfterTests
{
    private static readonly DateTimeOffset Now = MarketTestHarness.Start;

    [Fact]
    public void M16_DeltaSecondsAboveTheCeiling_IsClampedToSixtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), RetryAfterPolicy.HeaderDelay("120", Now));
    }

    [Fact]
    public void M17_HttpDateThirtySecondsAway_IsThirtySeconds()
    {
        var httpDate = Now.AddSeconds(30).UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfterPolicy.HeaderDelay(httpDate, Now));
    }

    [Fact]
    public void M18_NegativeDelta_IsZero()
    {
        // HttpResponseMessage.Headers.RetryAfter cannot even represent this value — it parses as
        // absent — which is why the raw header string is parsed here instead.
        Assert.Equal(TimeSpan.Zero, RetryAfterPolicy.HeaderDelay("-5", Now));
    }

    [Fact]
    public void M18_PastHttpDate_IsZero()
    {
        var httpDate = Now.AddMinutes(-5).UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

        Assert.Equal(TimeSpan.Zero, RetryAfterPolicy.HeaderDelay(httpDate, Now));
    }

    [Fact]
    public void AbsentOrUnparseableHeader_LeavesTheBackoffAlone()
    {
        Assert.Null(RetryAfterPolicy.HeaderDelay(null, Now));
        Assert.Null(RetryAfterPolicy.HeaderDelay("   ", Now));
        Assert.Null(RetryAfterPolicy.HeaderDelay("soon", Now));
        Assert.Equal(TimeSpan.FromSeconds(4), RetryAfterPolicy.Wait(null, TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void TheHeaderIsAFloorAndNeverACeiling()
    {
        Assert.Equal(TimeSpan.FromSeconds(8), RetryAfterPolicy.Wait(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfterPolicy.Wait(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public async Task Exhausted429s_FailWithRateLimitedAfterFourAttempts()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = MarketTestHarness.Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", "1");
            return response;
        });

        var client = MarketTestHarness.CreateClient(handler, out var time);

        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, held: null, CancellationToken.None))
            .ConfigureAwait(false);

        var why = CategoryFetchTests.Why(result);
        Assert.Equal(FailureKind.RateLimited, why.Kind);
        Assert.Equal(429, why.HttpStatus);

        // One initial attempt plus three retries, all inside a single gateway slot.
        Assert.Equal(1 + MarketClient.MaxRetries, handler.Calls);
    }

    [Fact]
    public async Task ServerErrorsAreRetried_AndASuccessAfterOneFailureStillMaps()
    {
        var body = MarketTestHarness.Overview(2, MarketTestHarness.GoodLine);
        var handler = new StubHandler((_, index) => index == 0
            ? MarketTestHarness.Json("boom", HttpStatusCode.ServiceUnavailable)
            : MarketTestHarness.Json(body));

        var client = MarketTestHarness.CreateClient(handler, out var time);

        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, held: null, CancellationToken.None))
            .ConfigureAwait(false);

        Assert.Equal(2, CategoryFetchTests.Value(result).Items.Count);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task NonRetriableStatus_IsReportedImmediatelyWithoutRetrying()
    {
        var handler = new StubHandler((_, _) => MarketTestHarness.Json("nope", HttpStatusCode.NotFound));
        var client = MarketTestHarness.CreateClient(handler, out var time);

        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, held: null, CancellationToken.None))
            .ConfigureAwait(false);

        var why = CategoryFetchTests.Why(result);
        Assert.Equal(FailureKind.HttpStatus, why.Kind);
        Assert.Equal(404, why.HttpStatus);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TransportException_IsRetriedAndThenReportedAsNetwork()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("no route"));
        var client = MarketTestHarness.CreateClient(handler, out var time);

        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, held: null, CancellationToken.None))
            .ConfigureAwait(false);

        Assert.Equal(FailureKind.Network, CategoryFetchTests.Why(result).Kind);
        Assert.Equal(1 + MarketClient.MaxRetries, handler.Calls);
    }
}
