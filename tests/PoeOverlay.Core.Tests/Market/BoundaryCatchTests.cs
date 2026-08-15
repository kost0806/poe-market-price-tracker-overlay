using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Tests.TestSupport;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M23 — the D-MK4 boundary catch on the two entry points.
/// </summary>
/// <remarks>
/// Step 2' closes the known hole. This catch closes the unknown ones: an exception that is neither
/// <c>JsonException</c> nor <c>HttpRequestException</c> would otherwise break the failure-as-value
/// contract and, on a user-initiated fetch, surface on the UI thread. The first edition had no
/// Market row in the allow-list at all, and the absence of the row was the structural cause of the
/// missing catch.
/// </remarks>
public sealed class BoundaryCatchTests
{
    private static MarketClient CreateFaultingClient(out RecordingLogger<MarketClient> logger)
    {
        var time = new FakeTimeProvider(MarketTestHarness.Start);
        logger = new RecordingLogger<MarketClient>();
        return new MarketClient(new ThrowingHttpClientFactory(), new NinjaGateway(time), time, logger);
    }

    [Fact]
    public async Task M23_UnexpectedExceptionOnTheCategoryPath_BecomesFailMappingFault()
    {
        var client = CreateFaultingClient(out var logger);

        var result = await client
            .FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, CancellationToken.None)
            .ConfigureAwait(false);

        var why = CategoryFetchTests.Why(result);
        Assert.Equal(FailureKind.MappingFault, why.Kind);
        Assert.Equal("MappingFault", why.Code);
        Assert.Equal("InvalidOperationException", why.ExceptionType);

        // The catch has an observable result on both axes required by D15: a value and a record.
        Assert.Single(logger.WithCode("MappingFault"));
    }

    [Fact]
    public async Task M23_UnexpectedExceptionOnTheLeaguePath_BecomesFailMappingFault()
    {
        var client = CreateFaultingClient(out var logger);

        var result = await client.FetchLeaguesAsync(RequestPriority.Polling, CancellationToken.None).ConfigureAwait(false);

        var why = Assert.IsType<MarketResult<LeagueList>.Fail>(result).Why;
        Assert.Equal(FailureKind.MappingFault, why.Kind);
        Assert.Single(logger.WithCode("MappingFault"));
    }
}
