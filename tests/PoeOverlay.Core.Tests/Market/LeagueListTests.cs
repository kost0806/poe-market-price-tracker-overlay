using System.Net;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M13–M15 — the league list judge of S2 5.9. Market renders the verdict; turning
/// Suspicious into an unresolved league is Polling's decision.
/// </summary>
public sealed class LeagueListTests
{
    private static async Task<LeagueList> FetchAsync(StubHandler handler)
    {
        var client = MarketTestHarness.CreateClient(handler, out var time);
        var result = await MarketTestHarness.RunAsync(
            time,
            client.FetchLeaguesAsync(RequestPriority.Polling, CancellationToken.None)).ConfigureAwait(false);

        return Assert.IsType<MarketResult<LeagueList>.Ok>(result).Value;
    }

    [Fact]
    public async Task M13_MeasuredFourLeagueArray_IsOkAndKeepsTheOrder()
    {
        var list = await FetchAsync(new StubHandler(MarketTestHarness.Fixture("leagues-measured.json"))).ConfigureAwait(false);

        Assert.Equal(LeagueListStatus.Ok, list.Status);
        Assert.Null(list.FailureCode);

        // Array order is the only signal of which league is the current challenge league; sorting
        // would destroy the one thing the endpoint tells us.
        Assert.Equal(
            new[] { "Allflame", "Hardcore Allflame", "Standard", "Hardcore" },
            list.Entries.Select(e => e.Id));
    }

    [Fact]
    public async Task M14_EmptyArray_IsFailedWithEmptyLeagueList()
    {
        var list = await FetchAsync(new StubHandler("[]")).ConfigureAwait(false);

        Assert.Equal(LeagueListStatus.Failed, list.Status);
        Assert.Equal("EmptyLeagueList", list.FailureCode);
        Assert.Empty(list.Entries);
    }

    [Fact]
    public async Task M15_HeadIsStandard_IsSuspiciousAndStillCarriesEveryEntry()
    {
        var body = """
            [{"id":"Standard","name":"Standard"},
             {"id":"Hardcore","name":"Hardcore"},
             {"id":"Allflame","name":"Allflame"},
             {"id":"Hardcore Allflame","name":"Hardcore Allflame"}]
            """;

        var list = await FetchAsync(new StubHandler(body)).ConfigureAwait(false);

        Assert.Equal(LeagueListStatus.Suspicious, list.Status);

        // A suspicious list is still a usable list: the manual selection dropdown must not be empty.
        Assert.Equal(4, list.Entries.Count);
    }

    [Fact]
    public async Task BlankAndDuplicateIds_AreDroppedWithTheFirstWinning()
    {
        var body = """
            [{"id":"  ","name":"blank"},
             {"id":"Allflame","name":"Allflame"},
             {"id":"Allflame","name":"Duplicate"},
             {"id":"Standard"}]
            """;

        var list = await FetchAsync(new StubHandler(body)).ConfigureAwait(false);

        Assert.Equal(LeagueListStatus.Ok, list.Status);
        Assert.Equal(new[] { "Allflame", "Standard" }, list.Entries.Select(e => e.Id));
        Assert.Equal("Allflame", list.Entries[0].Name);
        Assert.Equal("Standard", list.Entries[1].Name);
    }

    [Fact]
    public async Task HttpFailure_IsAFailedVerdictCarryingTheHttpCode()
    {
        var handler = new StubHandler((_, _) => MarketTestHarness.Json("nope", HttpStatusCode.NotFound));

        var list = await FetchAsync(handler).ConfigureAwait(false);

        Assert.Equal(LeagueListStatus.Failed, list.Status);
        Assert.Equal("HttpStatus", list.FailureCode);
    }

    [Fact]
    public async Task LeagueOrderAnomaly_IsWarnedAboutOncePerSession()
    {
        var body = """[{"id":"Standard","name":"Standard"}]""";
        var handler = new StubHandler(body);
        var client = MarketTestHarness.CreateClient(handler, out var time, out var logger);

        await MarketTestHarness.RunAsync(time, client.FetchLeaguesAsync(RequestPriority.Polling, CancellationToken.None))
            .ConfigureAwait(false);
        await MarketTestHarness.RunAsync(time, client.FetchLeaguesAsync(RequestPriority.Polling, CancellationToken.None))
            .ConfigureAwait(false);

        Assert.Single(logger.WithCode("LeagueOrderAnomaly"));
    }
}
