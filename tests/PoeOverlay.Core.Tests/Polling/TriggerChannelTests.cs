using System.Globalization;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Polling;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL20 (S4 16.5) — the B7 regression: a repoll requested right after a tick-won round.
/// </summary>
/// <remarks>
/// The first design raced <c>Task.WhenAny(timerTask, semaphore.WaitAsync())</c>. When the tick won,
/// the abandoned <c>WaitAsync</c> stayed queued and consumed the next <c>Release()</c>, so the live
/// waiter never saw the signal: every tick-won round silently swallowed one repoll, and the
/// heartbeat stayed healthy throughout. These tests are written so that they fail under that bug.
/// </remarks>
public sealed class TriggerChannelTests
{
    private static AppSettingsWatchlistEdit Edit(PollingHarness harness, params ExchangeCategory[] categories)
        => new(harness, categories);

    /// <summary>
    /// The trigger the loop decided a round had, read back from its <c>RoundStarted</c> line.
    /// </summary>
    /// <remarks>
    /// The log is the only place the merged trigger surfaces: the heartbeat records the round
    /// number and the outcome but not what started the round, and the request set is taken from the
    /// current watchlist whatever the trigger was. It is also the observable a maintainer reads when
    /// an edit appears to have done nothing, so it is the right thing to hold to.
    /// </remarks>
    private static RoundTrigger TriggerOf(PollingHarness harness, int roundNumber)
    {
        var prefix = $"Round {roundNumber.ToString(CultureInfo.InvariantCulture)} started (";
        var line = Assert.Single(
            harness.Logger.WithCode("RoundStarted"),
            e => e.Message.StartsWith(prefix, StringComparison.Ordinal));

        return Enum.Parse<RoundTrigger>(line.Message[prefix.Length..^2]);
    }

    [Fact]
    public async Task PL20_ARepollRequestedAfterATickWonRound_StillRuns()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        // Round two comes from the periodic timer: the tick wins.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);

        Edit(harness, ExchangeCategory.Scarab).Apply();

        // The floor puts the repoll a minute after round two finished; no tick can arrive before
        // t=10 min, so a third round can only come from the repoll itself.
        harness.Time.Advance(TimeSpan.FromSeconds(61));
        await harness.RunRoundAsync(3);

        Assert.Equal(3, harness.Rounds.Count);
        Assert.Contains(ExchangeCategory.Scarab, harness.Market.Rounds[2]);
        Assert.True(harness.Time.GetUtcNow() < PollingTestHarness.Start.AddMinutes(10));
    }

    [Fact]
    public async Task ARepollRaisedWhileARoundIsRunning_WaitsInTheChannelInsteadOfNesting()
    {
        using var harness = await PollingHarness.CreateAsync();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Market.Hold = hold;

        await harness.StartAsync();
        await harness.Market.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // The edit cancels the round in flight and schedules a repoll; the repoll must not start
        // while the first round is still unwinding.
        Edit(harness, ExchangeCategory.Scarab).Apply();
        harness.Time.Advance(TimeSpan.FromSeconds(61));
        Assert.Empty(harness.Rounds);

        hold.SetResult(true);
        harness.Market.Hold = null;

        await harness.RunRoundAsync(2);
        Assert.Equal(RoundOutcome.Canceled, harness.Rounds[0].Outcome);
        Assert.Contains(ExchangeCategory.Scarab, harness.Market.Rounds[1]);
    }

    [Fact]
    public async Task SeveralTriggersWaitingTogether_CollapseIntoOneRoundThatIsStillTheRepoll()
    {
        using var harness = await PollingHarness.CreateAsync();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Market.Hold = hold;

        // The edit below cancels the round in flight, and the loop cannot read a trigger until that
        // round has unwound. Parking the fetch across the cancellation keeps the whole backlog
        // behind round one instead of racing round one's continuation for it.
        harness.Market.HoldIgnoresCancellation = true;

        await harness.StartAsync();
        await harness.Market.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Two ticks, then the repoll the watchlist edit asks for, then a third tick — four triggers
        // in the channel while the first round is held open. The trailing tick is the whole point:
        // it is the ordering under which "the last trigger wins" and "a repoll dominates" disagree.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        Edit(harness, ExchangeCategory.Scarab).Apply();
        harness.Time.Advance(TimeSpan.FromSeconds(61));
        harness.Time.Advance(TimeSpan.FromMinutes(5));

        hold.SetResult(true);
        harness.Market.Hold = null;

        await harness.RunRoundAsync(2);
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(3);

        // Coalescing merges everything readable at once, so the backlog does not turn into a burst
        // of rounds against poe.ninja (NFR-02).
        Assert.Equal(3, harness.Rounds.Count);

        // And the one round it leaves is the repoll, not the tick that happened to arrive last. A
        // merge that let the tick win would swallow the edit exactly the way the dropped semaphore
        // waiter did, and the round count alone cannot tell the two apart.
        Assert.Equal(RoundTrigger.Repoll, TriggerOf(harness, 2));
        Assert.Contains(ExchangeCategory.Scarab, harness.Market.Rounds[1]);
    }

    [Fact]
    public async Task AfterShutdown_ADroppedTriggerIsReportedRatherThanIgnored()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.Service.StopAsync(CancellationToken.None);

        // TryWrite returns false once the channel is completed; ignoring that return value is how a
        // trigger disappears without trace.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        harness.Time.Advance(TimeSpan.FromMinutes(5));

        Assert.Single(harness.Rounds);
    }

    /// <summary>A watchlist edit that always adds a category the store has never fetched.</summary>
    private sealed class AppSettingsWatchlistEdit(PollingHarness harness, ExchangeCategory[] categories)
    {
        public void Apply()
        {
            harness.Settings.Update(harness.Settings.Current with
            {
                Watchlist = new EquatableArray<WatchlistEntry>(categories.Select((c, i) =>
                    new WatchlistEntry(new ItemId($"item{i}"), new CategoryRef(c.ToString(), c), null))),
            });
        }
    }
}
