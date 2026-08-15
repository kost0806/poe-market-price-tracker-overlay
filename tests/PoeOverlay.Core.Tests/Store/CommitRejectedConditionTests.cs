using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Store;
using Xunit;

namespace PoeOverlay.Core.Tests.Store;

/// <summary>
/// S2 11.8 S8, S8′ — D-ST4, the consumer that <c>RejectedCommitCount</c> never had.
/// </summary>
/// <remarks>
/// Without this the screen freezes indefinitely with every indicator healthy: failures are
/// validated too, so ConsecutiveFailures stays at zero; heartbeats are not validated, so
/// PollingStopped never fires; the round reports Completed and DisplayState says Ready. The user
/// keeps seeing the prices from the moment rejection began, formatted perfectly normally.
/// </remarks>
public sealed class CommitRejectedConditionTests
{
    private static void Round(StoreHarness harness, bool land)
    {
        harness.Store.RecordHeartbeatAttempt(1);
        if (land)
        {
            harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        }
        else
        {
            // The realistic trigger is not a bug: a league typed with a trailing space produces a
            // tag that mismatches every round.
            harness.Store.CommitCategory(new DataTag("Allflame ", 1), StoreTestHarness.Snapshot());
        }

        harness.Store.RecordHeartbeatOutcome(RoundOutcome.Completed);
    }

    [Fact]
    public async Task S8_TwoConsecutiveRoundsWithoutALandedCommit_RaiseTheCondition()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        Round(harness, land: false);
        await harness.WaitForVersionAsync(4).ConfigureAwait(false);

        Assert.False(harness.Current.Conditions.ContainsKey(AppConditionKind.CommitRejected));
        Assert.Equal(1, harness.Current.ConsecutiveEmptyCommitRounds);

        Round(harness, land: false);
        await harness.WaitForVersionAsync(7).ConfigureAwait(false);

        var condition = harness.Current.Conditions[AppConditionKind.CommitRejected];
        Assert.True(condition.Active);
        Assert.Equal(RejectionCodes.LeagueMismatch, condition.Detail);
        Assert.Equal(2, harness.Current.ConsecutiveEmptyCommitRounds);
        Assert.Equal(2, harness.Current.RejectedCommitCount);
    }

    [Fact]
    public async Task S8Prime_ALandedCommitInTheNextRound_ClearsTheCondition()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        Round(harness, land: false);
        Round(harness, land: false);
        await harness.WaitForVersionAsync(7).ConfigureAwait(false);
        Assert.True(harness.Current.Conditions[AppConditionKind.CommitRejected].Active);

        Round(harness, land: true);
        await harness.WaitForVersionAsync(10).ConfigureAwait(false);

        Assert.False(harness.Current.Conditions[AppConditionKind.CommitRejected].Active);
        Assert.Equal(0, harness.Current.ConsecutiveEmptyCommitRounds);
        Assert.Single(harness.Current.Categories);
    }

    [Fact]
    public async Task ACancelledRoundThatLandedNothing_IsNotCountedAsAnEmptyRound()
    {
        // S2 7.8: cancellation is not rejection and must not reach RejectedCommitCount. A cancelled
        // round lands nothing *because it was cancelled*, so it is evidence of nothing. Two ordinary
        // debounced edits back to back (S2 7.7: a league change, then a watchlist change) used to be
        // enough to raise the banner — with a Detail of null, because Validate never ran.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);

        for (var round = 1; round <= 2; round++)
        {
            harness.Store.RecordHeartbeatAttempt(round);
            harness.Store.RecordHeartbeatOutcome(RoundOutcome.Canceled);
        }

        await harness.WaitForVersionAsync(5).ConfigureAwait(false);

        Assert.False(harness.Current.Conditions[AppConditionKind.CommitRejected].Active);
        Assert.Equal(0, harness.Current.ConsecutiveEmptyCommitRounds);
        Assert.Equal(0, harness.Current.RejectedCommitCount);
        Assert.Equal(RoundOutcome.Canceled, harness.Current.Heartbeat.LastOutcome);
    }

    [Fact]
    public async Task ACommitThatLandedBeforeCancellation_StillResetsTheStreak()
    {
        // The other half of S2 7.8: commits made before the cancellation stay, so they are real
        // evidence that the round reached the store. Only the empty cancelled round is neutral.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        Round(harness, land: false);
        Round(harness, land: false);
        await harness.WaitForVersionAsync(7).ConfigureAwait(false);
        Assert.True(harness.Current.Conditions[AppConditionKind.CommitRejected].Active);

        harness.Store.RecordHeartbeatAttempt(3);
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        harness.Store.RecordHeartbeatOutcome(RoundOutcome.Canceled);
        await harness.WaitForVersionAsync(10).ConfigureAwait(false);

        Assert.False(harness.Current.Conditions[AppConditionKind.CommitRejected].Active);
        Assert.Equal(0, harness.Current.ConsecutiveEmptyCommitRounds);
    }

    [Fact]
    public async Task BeginNewLeague_ClearsARaisedCondition()
    {
        // INV-8 moves the whole data world at once. A rejection streak is state derived from the old
        // world; leaving the banner up would accuse the new one of the old one's fault.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        Round(harness, land: false);
        Round(harness, land: false);
        await harness.WaitForVersionAsync(7).ConfigureAwait(false);
        Assert.True(harness.Current.Conditions[AppConditionKind.CommitRejected].Active);

        harness.Store.BeginNewLeague("Standard", 2);
        await harness.WaitForVersionAsync(8).ConfigureAwait(false);

        Assert.False(harness.Current.Conditions[AppConditionKind.CommitRejected].Active);
        Assert.Equal(0, harness.Current.ConsecutiveEmptyCommitRounds);
    }

    [Fact]
    public async Task BeginNewLeague_DoesNotCarryAnEmptyRoundIntoTheNewLeague()
    {
        // The other direction: one stale empty round from the old league plus one slow first round
        // in the new one is an ordinary cold start, not a fault worth a banner.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        Round(harness, land: false);
        await harness.WaitForVersionAsync(4).ConfigureAwait(false);
        Assert.Equal(1, harness.Current.ConsecutiveEmptyCommitRounds);

        harness.Store.BeginNewLeague("Standard", 2);
        await harness.WaitForVersionAsync(5).ConfigureAwait(false);
        Assert.Equal(0, harness.Current.ConsecutiveEmptyCommitRounds);

        harness.Store.RecordHeartbeatAttempt(1);
        harness.Store.RecordHeartbeatOutcome(RoundOutcome.Completed);
        await harness.WaitForVersionAsync(7).ConfigureAwait(false);

        Assert.Equal(1, harness.Current.ConsecutiveEmptyCommitRounds);
        Assert.False(
            harness.Current.Conditions.TryGetValue(AppConditionKind.CommitRejected, out var condition)
            && condition.Active);
    }

    [Fact]
    public async Task ARecordedFailureCountsAsALandedCommit()
    {
        // A failure landing is proof that the round reached the store, which is what the condition
        // is actually about.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        for (var round = 0; round < 3; round++)
        {
            harness.Store.RecordHeartbeatAttempt(round);
            harness.Store.RecordCategoryFailure(StoreTestHarness.Tag, ExchangeCategory.Currency, StoreTestHarness.Failure());
            harness.Store.RecordHeartbeatOutcome(RoundOutcome.AllFailed);
        }

        await harness.WaitForVersionAsync(10).ConfigureAwait(false);

        Assert.False(harness.Current.Conditions[AppConditionKind.CommitRejected].Active);
        Assert.Equal(0, harness.Current.ConsecutiveEmptyCommitRounds);
        Assert.Equal(3, harness.Current.CategoryStatuses[ExchangeCategory.Currency].ConsecutiveFailures);
    }
}
