using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Composition;
using Xunit;

namespace PoeOverlay.Shell.Tests.Composition;

/// <summary>The boot stall watchdog (S3 D-SH19 / S4 18.1).</summary>
public sealed class BootWatchdogTests
{
    [Fact]
    public void DisarmBeforeTheTimeout_NeverFires()
    {
        var clock = new FakeTimeProvider();
        var fired = 0;
        using var watchdog = new BootWatchdog(clock, () => Interlocked.Increment(ref fired));

        watchdog.Arm();
        clock.Advance(TimeSpan.FromSeconds(10));
        watchdog.Disarm();
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(0, fired);
    }

    [Fact]
    public void AStalledBoot_Fires()
    {
        var clock = new FakeTimeProvider();
        var fired = 0;
        using var watchdog = new BootWatchdog(clock, () => Interlocked.Increment(ref fired));

        watchdog.Arm();
        clock.Advance(TimeSpan.FromSeconds(16));

        Assert.Equal(1, fired);
    }

    [Fact]
    public void ItFiresAtMostOnce()
    {
        var clock = new FakeTimeProvider();
        var fired = 0;
        using var watchdog = new BootWatchdog(clock, () => Interlocked.Increment(ref fired));

        watchdog.Arm();
        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(1, fired);
    }

    [Fact]
    public void TheTimeoutIsWellClearOfANormalBoot()
    {
        // A normal boot crosses steps 8–11 in hundreds of milliseconds; fifteen seconds is tens of
        // times that, and still short enough to beat a user's patience.
        Assert.True(ShellConstants.BootWatchdogTimeout >= TimeSpan.FromSeconds(10));
        Assert.True(ShellConstants.BootWatchdogTimeout <= TimeSpan.FromSeconds(30));
    }
}
