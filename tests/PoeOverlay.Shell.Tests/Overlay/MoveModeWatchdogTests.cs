using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Composition;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Overlay;
using Xunit;

namespace PoeOverlay.Shell.Tests.Overlay;

/// <summary>The move-mode inactivity watchdog (S3 4.6.1).</summary>
public sealed class MoveModeWatchdogTests
{
    [Fact]
    public void IdlePastTheThreshold_Fires()
    {
        var clock = new FakeTimeProvider();
        var dispatcher = new InlineDispatcher();
        var fired = 0;
        using var watchdog = new MoveModeWatchdog(dispatcher, clock, () => fired++);

        watchdog.Kick();
        clock.Advance(ShellConstants.MoveModeIdleThreshold + TimeSpan.FromSeconds(1));

        Assert.Equal(1, fired);
    }

    [Fact]
    public void ActivityResetsTheCountdown()
    {
        var clock = new FakeTimeProvider();
        var dispatcher = new InlineDispatcher();
        var fired = 0;
        using var watchdog = new MoveModeWatchdog(dispatcher, clock, () => fired++);

        watchdog.Kick();
        clock.Advance(ShellConstants.MoveModeIdleThreshold - TimeSpan.FromSeconds(5));
        watchdog.Kick();
        clock.Advance(ShellConstants.MoveModeIdleThreshold - TimeSpan.FromSeconds(5));

        Assert.Equal(0, fired);
    }

    [Fact]
    public void StopSilencesIt()
    {
        var clock = new FakeTimeProvider();
        var dispatcher = new InlineDispatcher();
        var fired = 0;
        using var watchdog = new MoveModeWatchdog(dispatcher, clock, () => fired++);

        watchdog.Kick();
        watchdog.Stop();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, fired);
    }

    [Fact]
    public void DisposeSilencesIt()
    {
        // Not optional: this is a managed TimeProvider resource, not an OS one reclaimed with the
        // HWND, and a live expiry can post into a half-dismantled application (S3 4.6.2 M7).
        var clock = new FakeTimeProvider();
        var dispatcher = new InlineDispatcher();
        var fired = 0;
        var watchdog = new MoveModeWatchdog(dispatcher, clock, () => fired++);

        watchdog.Kick();
        watchdog.Dispose();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, fired);
    }

    [Fact]
    public void TheTimeoutIsMarshalled_NotRunOnTheTimerThread()
    {
        // The callback arrives on a pool thread and every piece of move-mode state is UI-thread
        // affine, so the callback's only act may be to post (S3 4.6.1 M3).
        var clock = new FakeTimeProvider();
        var dispatcher = new InlineDispatcher();
        using var watchdog = new MoveModeWatchdog(dispatcher, clock, () => { });

        watchdog.Kick();
        clock.Advance(ShellConstants.MoveModeIdleThreshold + TimeSpan.FromSeconds(1));

        Assert.Equal(1, dispatcher.PostCount);
    }

    /// <summary>Runs posted work inline; HLD 3.4 already assumed a stub like this exists.</summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        internal int PostCount { get; private set; }

        public bool HasShutdownStarted => false;

        public bool CheckAccess() => true;

        public void Post(Action action, UiPostPriority priority = UiPostPriority.Normal)
        {
            PostCount++;
            action();
        }
    }
}
