using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Polling;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// The gateway-stall watch: the consumer <c>NinjaGateway.ActiveCount</c>/<c>QueuedCount</c> never
/// had.
/// </summary>
/// <remarks>
/// A leaked slot makes every category time out forever, and without this the symptom is
/// indistinguishable from poe.ninja being down: the failures are classified as Timeout, the
/// cooldowns grow, the heartbeat stays healthy and nothing anywhere says the fault is local.
/// </remarks>
public sealed class GatewayStallTests
{
    private static GatewayStallMonitor Monitor() => new(TimeSpan.FromSeconds(30));

    [Fact]
    public void AHealthyGateway_NeverReports()
    {
        var monitor = Monitor();
        var now = PollingTestHarness.Start;

        for (var i = 0; i < 200; i++)
        {
            // Requests in flight, with or without a queue behind them.
            Assert.False(monitor.Observe(2, 3, now.AddSeconds(i)));
            Assert.False(monitor.Observe(1, 0, now.AddSeconds(i)));
            Assert.False(monitor.Observe(0, 0, now.AddSeconds(i)));
        }

        Assert.Null(monitor.StalledSince);
    }

    [Fact]
    public void AQueueWithNothingInFlight_ReportsOnlyOnceTheThresholdHasPassed()
    {
        var monitor = Monitor();
        var now = PollingTestHarness.Start;

        Assert.False(monitor.Observe(0, 1, now));
        Assert.Equal(now, monitor.StalledSince);

        // The 250 ms issue floor is the only healthy way to see this pair, so anything short of the
        // threshold is not evidence of a leak.
        Assert.False(monitor.Observe(0, 1, now.AddSeconds(29)));
        Assert.True(monitor.Observe(0, 1, now.AddSeconds(30)));
    }

    [Fact]
    public void AContinuingStall_IsReportedOncePerEpisode()
    {
        var monitor = Monitor();
        var now = PollingTestHarness.Start;

        monitor.Observe(0, 2, now);
        Assert.True(monitor.Observe(0, 2, now.AddSeconds(30)));

        for (var i = 31; i < 400; i++)
        {
            Assert.False(monitor.Observe(0, 2, now.AddSeconds(i)));
        }
    }

    [Fact]
    public void AStallThatClearsAndReturns_IsANewEpisode()
    {
        var monitor = Monitor();
        var now = PollingTestHarness.Start;

        monitor.Observe(0, 1, now);
        Assert.True(monitor.Observe(0, 1, now.AddSeconds(30)));

        // Recovery resets the clock, so the next episode has to earn its own threshold.
        Assert.False(monitor.Observe(1, 1, now.AddSeconds(31)));
        Assert.Null(monitor.StalledSince);

        Assert.False(monitor.Observe(0, 1, now.AddSeconds(40)));
        Assert.False(monitor.Observe(0, 1, now.AddSeconds(69)));
        Assert.True(monitor.Observe(0, 1, now.AddSeconds(70)));
    }

    [Fact]
    public async Task AStalledGatewayDuringARound_ReachesTheStoreAndTheLog()
    {
        using var harness = await PollingHarness.CreateAsync();
        using var observed = new SemaphoreSlim(0);
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Market.Hold = hold;
        harness.Service.GatewayLoadSampler = () =>
        {
            observed.Release();
            return (0, 1);
        };

        await harness.StartAsync();
        await harness.Market.FetchEnteredAsync(1).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // The sampler is a real scheduler, so each step gives it a chance to run before the clock
        // moves again. The assertion below is on state, never on how long any of this took.
        for (var i = 0; i < 20 && harness.Current.LastError is null; i++)
        {
            harness.Time.Advance(PollingOptions.GatewayStallThreshold + TimeSpan.FromSeconds(1));
            await observed.WaitAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        }

        await harness.WaitForAsync(s => s.LastError?.Code == "GatewayStalled", "the stall was reported");

        Assert.Equal("Polling", harness.Current.LastError!.Module);
        Assert.Equal("queued=1 active=0", harness.Current.LastError.Detail);
        Assert.Contains(harness.Logger.WithCode("GatewayStalled"), e => e.Level == LogLevel.Error);

        hold.TrySetResult(true);
        harness.Market.Hold = null;
        await harness.WaitForRoundsAsync(1);
    }

    [Fact]
    public async Task ABusyGatewayDuringARound_IsNotReported()
    {
        using var harness = await PollingHarness.CreateAsync();
        using var observed = new SemaphoreSlim(0);
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Market.Hold = hold;

        // Both slots in flight with a queue behind them: entirely normal under eighteen categories.
        harness.Service.GatewayLoadSampler = () =>
        {
            observed.Release();
            return (2, 5);
        };

        await harness.StartAsync();
        await harness.Market.FetchEnteredAsync(1).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        for (var i = 0; i < 20; i++)
        {
            harness.Time.Advance(PollingOptions.GatewayStallThreshold + TimeSpan.FromSeconds(1));
            await observed.WaitAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        }

        hold.TrySetResult(true);
        harness.Market.Hold = null;
        await harness.RunRoundAsync(1);

        Assert.Null(harness.Current.LastError);
        Assert.Empty(harness.Logger.WithCode("GatewayStalled"));
    }

    [Fact]
    public async Task TheRealGatewayUnderLoad_ReadsAsHealthy()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.Gateway.AcquireAsync(RequestPriority.Polling, CancellationToken.None);

        // The gateway's 250 ms minimum issue interval applies to the second slot as well, so the
        // clock has to move before it can be granted.
        var second = harness.Gateway.AcquireAsync(RequestPriority.Polling, CancellationToken.None);
        harness.Time.Advance(NinjaGateway.MinIssueInterval);
        await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Both slots taken, so the pair the monitor watches for cannot arise: the counters the
        // service samples are the gateway's real ones.
        Assert.Equal(2, harness.Gateway.ActiveCount);
        Assert.Equal(0, harness.Gateway.QueuedCount);

        harness.Gateway.Release();
        harness.Gateway.Release();
        Assert.Equal(0, harness.Gateway.ActiveCount);
    }
}
