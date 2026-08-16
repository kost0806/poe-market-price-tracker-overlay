using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL21 – PL23 (S4 16.5) — round cancellation and the outermost finally.
/// </summary>
public sealed class CancellationTests
{
    private static void EditWatchlist(PollingHarness harness, params ExchangeCategory[] categories)
        => harness.Settings.Update(harness.Settings.Current with
        {
            Watchlist = new EquatableArray<WatchlistEntry>(categories.Select((c, i) =>
                new WatchlistEntry(new ItemId($"item{i}"), new CategoryRef(c.ToString(), c), null))),
        });

    [Fact]
    public async Task PL21_ARoundCancelledInFlight_CommitsNothingFurtherAndStillRecordsItsOutcome()
    {
        using var harness = await PollingHarness.CreateAsync();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Market.Hold = hold;

        await harness.StartAsync();
        await harness.Market.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        EditWatchlist(harness, ExchangeCategory.Scarab);
        hold.TrySetResult(true);

        await harness.WaitForRoundsAsync(1);
        Assert.Equal(RoundOutcome.Canceled, harness.Rounds[0].Outcome);

        await harness.WaitForAsync(
            s => s.Heartbeat.LastOutcome == RoundOutcome.Canceled, "the cancelled outcome was recorded");

        var snapshot = harness.Current;
        Assert.Empty(snapshot.Categories);

        // Cancellation is not contamination: the rejection counter must not move, or an edit-heavy
        // session would look like a store that is refusing data.
        Assert.Equal(0, snapshot.RejectedCommitCount);

        // Recording the outcome is what keeps LastRoundCompletedAt moving; without it the stall
        // verdict would fire against a loop that is being cancelled precisely because it is alive.
        Assert.NotNull(snapshot.Heartbeat.LastRoundCompletedAt);
    }

    [Fact]
    public async Task ACancelledRound_DoesNotCountAsACommitFreeRound()
    {
        using var harness = await PollingHarness.CreateAsync();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Market.Hold = hold;

        await harness.StartAsync();
        await harness.Market.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        EditWatchlist(harness, ExchangeCategory.Scarab);
        hold.TrySetResult(true);
        await harness.WaitForRoundsAsync(1);
        await harness.WaitForAsync(s => s.Heartbeat.LastOutcome is not null, "the outcome landed");

        Assert.Equal(0, harness.Current.ConsecutiveEmptyCommitRounds);
        Assert.False(
            harness.Current.Conditions.TryGetValue(AppConditionKind.CommitRejected, out var rejected)
            && rejected.Active);
    }

    [Fact]
    public async Task PL22_AnUnexpectedExceptionInTheLoop_ExitsAsFaultedAndSaysSo()
    {
        using var harness = await PollingHarness.CreateAsync();
        harness.Market.LeagueException = new InvalidOperationException("the league endpoint exploded");

        await harness.StartAsync();
        await harness.WaitForAsync(s => s.Heartbeat.LoopExited, "the loop exit was recorded");

        var snapshot = harness.Current;
        Assert.True(snapshot.Heartbeat.LoopExited);
        Assert.Equal(LoopExitKind.Faulted, snapshot.Heartbeat.ExitKind);
        Assert.NotNull(snapshot.Heartbeat.ExitedAt);

        Assert.Equal("LoopExited", snapshot.LastError!.Code);
        Assert.Equal("InvalidOperationException", snapshot.LastError.ExceptionType);
        Assert.Contains(harness.Logger.WithCode("LoopExited"), e => e.Level == LogLevel.Error);

        // The attempt was still recorded before the round died, so the round number moved (D20).
        Assert.Equal(1, snapshot.Heartbeat.LastRoundNumber);

        // No restart: a loop that died has a cause, and restarting hides it.
        harness.Time.Advance(TimeSpan.FromMinutes(30));
        Assert.Empty(harness.Rounds);
    }

    [Fact]
    public async Task PL23_AStoppedHost_ExitsAsCanceled()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        await harness.Service.StopAsync(CancellationToken.None);
        await harness.WaitForAsync(s => s.Heartbeat.LoopExited, "the loop exit was recorded");

        Assert.Equal(LoopExitKind.Canceled, harness.Current.Heartbeat.ExitKind);
    }

    [Fact]
    public async Task CommitsThatLandedBeforeACancellation_Survive()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.Count == 2, "the first round landed");

        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Market.Hold = hold;
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.Market.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        EditWatchlist(harness, ExchangeCategory.Scarab, ExchangeCategory.Fossil);
        hold.TrySetResult(true);
        await harness.WaitForRoundsAsync(2);

        // Discarding them would let an ordinary watchlist edit destroy data that is still perfectly
        // current — the epoch never moved.
        Assert.Equal(2, harness.Current.Categories.Count);
    }
}
