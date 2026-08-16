using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL18 (S4 16.5) — changing the refresh interval.
/// </summary>
/// <remarks>
/// Measured here, on net8.0 with FakeTimeProvider: a <c>PeriodicTimer.Period</c> change does reach
/// the tick already being awaited, but it restarts the wait <em>from the moment of the change</em>,
/// not from the moment the wait began. A five-minute wait three minutes old, switched to thirty
/// minutes, fires at t=33, not t=30; a thirty-minute wait one minute old, switched to five, fires
/// at t=6, not t=5.
/// <para>
/// S2 7.7's earlier measurement (a 3000 ms wait switched to 150 ms firing at 156 ms) is consistent
/// with both readings — the change was made about six milliseconds in — and the design took the
/// other one, so PL18's "the next tick is thirty minutes from the start" is off by however long the
/// wait had already run. The user-visible consequence the design cared about is unchanged and in
/// fact slightly worse: raising the interval stalls the screen for a full new period.
/// </para>
/// </remarks>
public sealed class PeriodChangeTests
{
    [Fact]
    public async Task PL18_RaisingTheInterval_MovesNeitherCounterAndCancelsNoRound()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        var epoch = harness.Service.DataEpoch;
        var generation = harness.Service.RoundGeneration;

        harness.Time.Advance(TimeSpan.FromMinutes(3));
        harness.Settings.Update(harness.Settings.Current with { RefreshIntervalMinutes = 30 });

        Assert.Equal(epoch, harness.Service.DataEpoch);
        Assert.Equal(generation, harness.Service.RoundGeneration);

        // The old five-minute deadline passes with nothing happening: the change reached the wait
        // that was already running.
        harness.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(harness.Rounds);
    }

    [Fact]
    public async Task PL18_TheLengthenedWait_RestartsFromTheChangeRatherThanFromTheStartOfTheWait()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        harness.Time.Advance(TimeSpan.FromMinutes(3));
        harness.Settings.Update(harness.Settings.Current with { RefreshIntervalMinutes = 30 });

        // t=30, which is where PL18 expected the tick. Nothing yet: the wait restarted at t=3.
        harness.Time.Advance(TimeSpan.FromMinutes(27));
        Assert.Single(harness.Rounds);

        harness.Time.Advance(TimeSpan.FromMinutes(3));
        await harness.RunRoundAsync(2);

        // Thirty minutes after the change. The user who raises the interval sees the screen hold
        // still for a full new period plus whatever the old wait had already run.
        Assert.Equal(PollingTestHarness.Start.AddMinutes(33), harness.Time.GetUtcNow());
    }

    [Fact]
    public async Task LoweringTheInterval_AlsoRestartsTheWaitFromTheChange()
    {
        using var harness = await PollingHarness.CreateAsync(PollingTestHarness.Settings(interval: 30));
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        harness.Time.Advance(TimeSpan.FromMinutes(1));
        harness.Settings.Update(harness.Settings.Current with { RefreshIntervalMinutes = 5 });

        harness.Time.Advance(TimeSpan.FromMinutes(4));
        Assert.Single(harness.Rounds);

        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.RunRoundAsync(2);

        Assert.Equal(PollingTestHarness.Start.AddMinutes(6), harness.Time.GetUtcNow());
    }
}
