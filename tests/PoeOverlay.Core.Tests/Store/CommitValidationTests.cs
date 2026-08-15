using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Store;
using Xunit;

namespace PoeOverlay.Core.Tests.Store;

/// <summary>
/// S2 11.8 S0–S4′ — commit validation (S2 6.4, codes in S4 13.4).
/// </summary>
public sealed class CommitValidationTests
{
    [Fact]
    public async Task S0_BeginNewLeagueThenCommit_TheCommitLands()
    {
        // B1 regression. The first edition validated commits against LeagueResolution.League, which
        // no producer ever set, so every commit of the first round was rejected and the app stayed
        // in Loading for ever. The absence of this row is why that survived review.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        var snapshot = harness.Current;
        Assert.Single(snapshot.Categories);
        Assert.Equal(LeagueResolutionState.Resolved, snapshot.LeagueResolution.State);
        Assert.Equal(StoreTestHarness.League, snapshot.LeagueResolution.League);
        Assert.Equal(StoreTestHarness.League, snapshot.DataLeague);
        Assert.Equal(0, snapshot.RejectedCommitCount);
    }

    [Fact]
    public async Task S1_EpochMismatch_IsRejectedAndCountedWithoutTouchingTheData()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 3);
        harness.Store.CommitCategory(new DataTag(StoreTestHarness.League, 3), StoreTestHarness.Snapshot(epoch: 3));
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        var before = harness.Current;

        harness.Store.CommitCategory(
            new DataTag(StoreTestHarness.League, 2),
            StoreTestHarness.Snapshot(ExchangeCategory.Scarab, epoch: 2));
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);

        var after = harness.Current;
        Assert.Equal(1, after.RejectedCommitCount);

        // The data slot keeps its very reference — a rejection cannot rewrite it.
        Assert.Same(before.Categories, after.Categories);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Single(harness.Logger.WithCode(RejectionCodes.EpochMismatch));
    }

    [Fact]
    public async Task S2_LeagueMismatch_IsRejected()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.CommitCategory(new DataTag("Standard", 1), StoreTestHarness.Snapshot(league: "Standard"));
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        Assert.Empty(harness.Current.Categories);
        Assert.Equal(1, harness.Current.RejectedCommitCount);
        Assert.Single(harness.Logger.WithCode(RejectionCodes.LeagueMismatch));
    }

    [Fact]
    public async Task S2Prime_DefaultDataTag_IsRejectedAsDefaultTag()
    {
        // The measured hole: default(DataTag) is (null, 0), and right after start-up the baseline is
        // (null, 0) too, so both != comparisons pass it. Only an explicit blank check catches it,
        // and it has to be a real check rather than a Debug.Assert.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 0);
        harness.Store.CommitCategory(default, StoreTestHarness.Snapshot(epoch: 0));
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        Assert.Empty(harness.Current.Categories);
        Assert.Equal(1, harness.Current.RejectedCommitCount);
        Assert.Single(harness.Logger.WithCode(RejectionCodes.DefaultTag));
    }

    [Fact]
    public async Task S2DoublePrime_CommitBeforeAnyBaseline_IsRejectedAsNoBaseline()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);

        Assert.Empty(harness.Current.Categories);
        Assert.Equal(1, harness.Current.RejectedCommitCount);
        Assert.Single(harness.Logger.WithCode(RejectionCodes.NoBaseline));
    }

    [Fact]
    public async Task S2TriplePrime_SnapshotCarryingAnEmptyItemIdKey_IsRejectedInReleaseToo()
    {
        // default(ItemId) works perfectly well as a dictionary key, so if the mapper lets one
        // through, nothing downstream would ever notice.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.SnapshotWithEmptyItemId());
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        Assert.Empty(harness.Current.Categories);
        Assert.Equal(1, harness.Current.RejectedCommitCount);
        Assert.Single(harness.Logger.WithCode(RejectionCodes.EmptyItemId));
    }

    [Fact]
    public async Task S3_HeartbeatRightAfterALeagueChange_IsAppliedBecauseItIsNotValidated()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.RecordHeartbeatAttempt(7);
        harness.Store.RecordHeartbeatOutcome(RoundOutcome.Completed);
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);

        // A heartbeat is a survival signal, not data: validating it would neutralise D20.
        Assert.Equal(7, harness.Current.Heartbeat.LastRoundNumber);
        Assert.Equal(RoundOutcome.Completed, harness.Current.Heartbeat.LastOutcome);
        Assert.Equal(0, harness.Current.RejectedCommitCount);
    }

    [Fact]
    public async Task S4_SetLeagueListWhileUnresolved_IsApplied()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.SetLeagueUnresolved("Suspicious");
        harness.Store.SetLeagueList(new LeagueList(
            [new LeagueEntry("Allflame", "Allflame")],
            StoreTestHarness.Start,
            LeagueListStatus.Ok,
            null));
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        // The league list is not data belonging to a league; it is what the user picks one from.
        Assert.NotNull(harness.Current.Leagues);
        Assert.Single(harness.Current.Leagues!.Entries);
        Assert.Equal(LeagueResolutionState.Unresolved, harness.Current.LeagueResolution.State);
    }

    [Fact]
    public async Task S4Prime_RetreatingToUnresolvedAfterASuccessfulRound_KeepsTheData()
    {
        // INV-5. The first edition's invariant could only be honoured by discarding data, which is
        // a direct violation of FR-03-3 ("keep showing the last good value").
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        harness.Store.CommitRate(StoreTestHarness.Tag, new DivineRate(194.6m, StoreTestHarness.Start, StoreTestHarness.League, false));
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);

        harness.Store.SetLeagueUnresolved("LeagueListInvalid");
        await harness.WaitForVersionAsync(4).ConfigureAwait(false);

        var snapshot = harness.Current;
        Assert.Single(snapshot.Categories);
        Assert.NotNull(snapshot.Rate);
        Assert.Equal(StoreTestHarness.League, snapshot.DataLeague);
        Assert.Equal(LeagueResolutionState.Unresolved, snapshot.LeagueResolution.State);
        Assert.Equal("LeagueListInvalid", snapshot.LeagueResolution.ReasonCode);
    }

    [Fact]
    public async Task BeginNewLeague_EmptiesEveryDataSlotInOneCommand()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        harness.Store.SetFetchedListing(StoreTestHarness.Tag, ExchangeCategory.Scarab, StoreTestHarness.Snapshot(ExchangeCategory.Scarab));
        harness.Store.CommitRate(StoreTestHarness.Tag, new DivineRate(194.6m, StoreTestHarness.Start, StoreTestHarness.League, false));
        await harness.WaitForVersionAsync(4).ConfigureAwait(false);

        harness.Store.BeginNewLeague("Standard", 2);
        await harness.WaitForVersionAsync(5).ConfigureAwait(false);

        var snapshot = harness.Current;

        // INV-8: no state exists in which only two of the three moved.
        Assert.Equal("Standard", snapshot.DataLeague);
        Assert.Equal(2, snapshot.DataEpoch);
        Assert.Equal(LeagueResolutionState.Resolved, snapshot.LeagueResolution.State);
        Assert.Empty(snapshot.Categories);
        Assert.Empty(snapshot.CategoryStatuses);
        Assert.Null(snapshot.Rate);
        Assert.Null(snapshot.Listing);
    }

    [Fact]
    public async Task RecordCategoryFailure_MovesTheStatusAndNeverTheData()
    {
        // D-D4 / PL25: FR-03-3 holds structurally because the failure path cannot reach the data.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);
        var before = harness.Current;

        harness.Store.RecordCategoryFailure(StoreTestHarness.Tag, ExchangeCategory.Currency, StoreTestHarness.Failure());
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);

        var after = harness.Current;
        Assert.Same(before.Categories, after.Categories);
        Assert.Equal(1, after.CategoryStatuses[ExchangeCategory.Currency].ConsecutiveFailures);
        Assert.Equal("Network", after.CategoryStatuses[ExchangeCategory.Currency].LastFailure!.Code);
    }

    [Fact]
    public async Task RejectionsDoNotTouchLastError()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);

        // A transient rejection is not an error to put in front of the user.
        Assert.Null(harness.Current.LastError);
        Assert.Equal(LogLevel.Warning, harness.Logger.WithCode(RejectionCodes.NoBaseline)[0].Level);
    }
}
