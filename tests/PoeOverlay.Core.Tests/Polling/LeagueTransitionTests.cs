using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Polling;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL19 / PL24 (S4 16.5) — settling on a league and moving to another one.
/// </summary>
public sealed class LeagueTransitionTests
{
    [Fact]
    public async Task PL19_ChangingTheLeague_BumpsBothCountersAndInvalidatesEverything()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.Count == 2 && s.Rate is not null, "the first league landed");

        var epoch = harness.Service.DataEpoch;
        var generation = harness.Service.RoundGeneration;
        Assert.Equal(PollingTestHarness.League, harness.Current.DataLeague);

        harness.Time.Advance(TimeSpan.FromSeconds(60));
        harness.Settings.Update(harness.Settings.Current with { League = "Standard" });

        // The generation moves immediately, so the round in flight stops committing at once.
        Assert.Equal(generation + 1, harness.Service.RoundGeneration);

        harness.Time.Advance(PollingOptions.RepollDebounceWindow);
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(s => s.DataLeague == "Standard", "the new league was committed");

        var snapshot = harness.Current;
        Assert.Equal(epoch + 1, harness.Service.DataEpoch);
        Assert.Equal(harness.Service.DataEpoch, snapshot.DataEpoch);
        Assert.Equal(LeagueResolutionState.Resolved, snapshot.LeagueResolution.State);

        // Every surviving snapshot belongs to the new world (INV-1 / INV-2).
        Assert.All(snapshot.Categories.Values, c =>
        {
            Assert.Equal("Standard", c.League);
            Assert.Equal(snapshot.DataEpoch, c.DataEpoch);
        });

        Assert.Equal("Standard", snapshot.Rate!.League);
        Assert.Equal(0, snapshot.RejectedCommitCount);
    }

    [Fact]
    public async Task PL24_AConfiguredLeagueWithTrailingSpace_IsTrimmedSoCommitsLand()
    {
        using var harness = await PollingHarness.CreateAsync(PollingTestHarness.Settings(league: "Allflame "));
        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.Count > 0, "the commit landed");

        // Untrimmed, the tag would never equal the baseline, every commit would be rejected, and the
        // heartbeat would stay perfectly healthy while the screen never changed.
        Assert.Equal("Allflame", harness.Current.DataLeague);
        Assert.Equal(0, harness.Current.RejectedCommitCount);
        Assert.True(harness.Current.Categories.ContainsKey(ExchangeCategory.Currency));
    }

    [Fact]
    public async Task AWhitespaceOnlyLeague_FallsBackToTheListRatherThanBeingUsed()
    {
        using var harness = await PollingHarness.CreateAsync(PollingTestHarness.Settings(league: "   "));
        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.DataLeague is not null, "a league was settled");

        Assert.Equal("Allflame", harness.Current.DataLeague);
    }

    [Fact]
    public async Task ASnapshotBelongingToAnotherLeague_FailsRatherThanBreakingINV1Silently()
    {
        using var harness = await PollingHarness.CreateAsync();
        harness.Market.StampLeague = false;
        harness.Market.Respond = (category, _) => PollingTestHarness.Ok(
            PollingTestHarness.Snapshot(category, league: "Standard"));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(
            s => s.CategoryStatuses.ContainsKey(ExchangeCategory.Currency), "the mismatch was recorded");

        // Commit validation reads the tag, not the snapshot, so without this guard the wrong-league
        // snapshot would land and INV-1 would be false with nothing reporting it.
        Assert.Empty(harness.Current.Categories);
        Assert.Equal("LeagueMismatch", harness.Current.CategoryStatuses[ExchangeCategory.Currency].LastFailure!.Code);
        Assert.Equal(RoundOutcome.AllFailed, harness.Rounds[0].Outcome);
    }

    [Fact]
    public void ResolveLeague_HonoursAnExplicitLeagueEvenWhenTheListFailed()
    {
        var failed = new LeagueList([], PollingTestHarness.Start, LeagueListStatus.Failed, "Network");

        var (state, league, reason) = PollingService.ResolveLeague("  Allflame  ", failed);

        Assert.Equal(LeagueResolutionState.Resolved, state);
        Assert.Equal("Allflame", league);
        Assert.Null(reason);
    }

    [Fact]
    public void ResolveLeague_TakesTheFirstEntryOfAnOkList()
    {
        var list = new LeagueList(
            [new LeagueEntry("Allflame", "Allflame"), new LeagueEntry("Standard", "Standard")],
            PollingTestHarness.Start,
            LeagueListStatus.Ok,
            null);

        // Array order is the only signal that names the current challenge league; sorting it would
        // destroy the one piece of information the endpoint provides.
        Assert.Equal("Allflame", PollingService.ResolveLeague(null, list).League);
    }

    [Fact]
    public void ResolveLeague_WithNoConfiguredLeagueAndABadList_NamesItsReason()
    {
        var suspicious = new LeagueList(
            [new LeagueEntry("Standard", "Standard")], PollingTestHarness.Start, LeagueListStatus.Suspicious, null);
        var failed = new LeagueList([], PollingTestHarness.Start, LeagueListStatus.Failed, "EmptyLeagueList");

        // Market fills in a failure code only for Failed, so a suspicious list would otherwise leave
        // the unresolved state with a null reason and the banner with nothing to say.
        Assert.Equal(
            (LeagueResolutionState.Unresolved, null, PollingService.SuspiciousLeagueListReason),
            PollingService.ResolveLeague(null, suspicious));

        Assert.Equal(
            (LeagueResolutionState.Unresolved, null, "EmptyLeagueList"),
            PollingService.ResolveLeague(null, failed));
    }

    [Fact]
    public async Task ARoundThatCannotSettleOnALeague_KeepsTheDataItAlreadyHas()
    {
        using var harness = await PollingHarness.CreateAsync(PollingTestHarness.Settings(league: null));
        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.Count > 0, "the first round landed");

        harness.Market.Leagues = new LeagueList([], PollingTestHarness.Start, LeagueListStatus.Failed, "Network");
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(
            s => s.LeagueResolution.State == LeagueResolutionState.Unresolved, "the retreat was published");

        // INV-5: only BeginNewLeague empties data. The first edition's invariant could be satisfied
        // only by throwing the prices away, which is a direct violation of FR-03-3.
        Assert.NotEmpty(harness.Current.Categories);
        Assert.NotNull(harness.Current.Rate);
        Assert.Equal(PollingTestHarness.League, harness.Current.DataLeague);
        Assert.True(harness.Current.Conditions[AppConditionKind.LeagueUnresolved].Active);
    }
}
