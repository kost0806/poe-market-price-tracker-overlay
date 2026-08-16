using PoeOverlay.Composition;
using PoeOverlay.Core.Presentation.Fanout;

namespace PoeOverlay.Overlay;

/// <summary>
/// The move-mode inactivity watchdog (S3 4.6.1 / S4 12.3).
/// </summary>
/// <remarks>
/// The callback arrives on a pool thread, and every piece of move-mode state — the capture, the
/// "expired" flag, the style bits — is UI-thread affine. So the callback's only act is to post; it
/// reads nothing and writes nothing itself (S3 4.6.1 M3).
/// <para>
/// Disposal is not optional. The extended-style bits and the capture are OS resources reclaimed
/// when the HWND dies, but this timer is a managed <c>TimeProvider</c> resource: left alive, its
/// expiry can still post into a half-dismantled application during teardown (S3 4.6.2 M7).
/// </para>
/// </remarks>
internal sealed class MoveModeWatchdog : IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly Action _onIdleTimeout;
    private readonly ITimer _timer;
    private bool _disposed;

    /// <summary>Creates a stopped watchdog.</summary>
    /// <param name="dispatcher">Where the timeout is marshalled to.</param>
    /// <param name="timeProvider">The clock, per S2 1.3.</param>
    /// <param name="onIdleTimeout">Runs on the UI thread when the idle threshold passes.</param>
    internal MoveModeWatchdog(IUiDispatcher dispatcher, TimeProvider timeProvider, Action onIdleTimeout)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(onIdleTimeout);

        _dispatcher = dispatcher;
        _onIdleTimeout = onIdleTimeout;
        _timer = timeProvider.CreateTimer(_ => OnElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Starts or restarts the idle countdown.</summary>
    internal void Kick()
    {
        if (_disposed)
        {
            return;
        }

        _ = _timer.Change(ShellConstants.MoveModeIdleThreshold, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Stops the countdown without disposing. Idempotent.</summary>
    internal void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _ = _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void OnElapsed()
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.Post(_onIdleTimeout);
    }
}
