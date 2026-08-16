using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Pricing;
using Xunit;
using StoreService = PoeOverlay.Core.Store.Store;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL0 – PL4 (S4 16.5) — the heartbeat the stall verdict reads.
/// </summary>
/// <remarks>
/// The verdict itself (S4 18.4) is assembled in Presentation, so these assert the heartbeat facts
/// it is computed from together with the threshold it compares against.
/// </remarks>
public sealed class HeartbeatTests
{
    [Fact]
    public void PL0_BeforeAnyRound_TheAttemptInstantIsAbsentRatherThanZero()
    {
        var heartbeat = StoreService.CreateInitialSnapshot().Heartbeat;

        // The whole point of the nullable: default(DateTimeOffset) is 0001-01-01, which is not
        // "absent" but "two thousand years ago". A non-nullable field would make the stall verdict
        // true on the very first thirty-second tick, putting a "polling has stopped" banner next to
        // a row that is still loading.
        Assert.Null(heartbeat.LastRoundAttemptAt);
        Assert.False(heartbeat.LoopExited);

        var thirtySecondsIn = PollingTestHarness.Start.AddSeconds(30);
        Assert.True(thirtySecondsIn - default(DateTimeOffset) > StalenessPolicy.HeartbeatStaleAfter(5));
    }

    [Fact]
    public async Task PL1_ARoundThatDiesInItsFirstStep_StillRecordsTheAttempt()
    {
        using var harness = await PollingHarness.CreateAsync(PollingTestHarness.Settings(league: null));

        // The league list fails and no league is configured, so the round returns before it makes a
        // single category request.
        harness.Market.Leagues = new LeagueList([], PollingTestHarness.Start, LeagueListStatus.Failed, "EmptyLeagueList");

        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        Assert.Equal(RoundOutcome.LeagueUnresolved, harness.Rounds[0].Outcome);
        Assert.NotNull(harness.Current.Heartbeat.LastRoundAttemptAt);
        Assert.Equal(1, harness.Current.Heartbeat.LastRoundNumber);
        Assert.Empty(harness.Market.Requested);
    }

    [Theory]
    [InlineData(10, 59, false)]
    [InlineData(11, 1, true)]
    public void PL2_PL3_TheStallThresholdIsElevenMinutesAtTheDefaultInterval(int minutes, int seconds, bool stalled)
    {
        var last = PollingTestHarness.Start;
        var now = last.AddMinutes(minutes).AddSeconds(seconds);

        Assert.Equal(TimeSpan.FromMinutes(11), StalenessPolicy.HeartbeatStaleAfter(5));
        Assert.Equal(stalled, now - last > StalenessPolicy.HeartbeatStaleAfter(5));
    }

    [Fact]
    public async Task PL4_AfterTheLoopExits_TheHeartbeatSaysSoRegardlessOfAge()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        await harness.Service.StopAsync(CancellationToken.None);
        await harness.WaitForAsync(s => s.Heartbeat.LoopExited, "the loop exit was recorded");

        var heartbeat = harness.Current.Heartbeat;
        Assert.True(heartbeat.LoopExited);
        Assert.Equal(LoopExitKind.Canceled, heartbeat.ExitKind);
        Assert.NotNull(heartbeat.ExitedAt);

        // The attempt is seconds old, so the age branch would say healthy; the exit branch outranks
        // it, because nothing will ever attempt another round.
        Assert.True(
            harness.Time.GetUtcNow() - heartbeat.LastRoundAttemptAt!.Value < StalenessPolicy.HeartbeatStaleAfter(5));
    }

    [Fact]
    public async Task RoundOne_RunsAtStartUpWithoutWaitingForTheFirstTick()
    {
        using var harness = await PollingHarness.CreateAsync();

        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        // Nothing has advanced the clock, so the periodic timer has not fired once. Without the
        // start-up round the first data would arrive five to sixty minutes after launch.
        Assert.Equal(PollingTestHarness.Start, harness.Time.GetUtcNow());
        Assert.Single(harness.Rounds);
        Assert.Equal(RoundOutcome.Completed, harness.Rounds[0].Outcome);
    }
}
