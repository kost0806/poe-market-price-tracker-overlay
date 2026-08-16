namespace PoeOverlay.Composition;

/// <summary>
/// Catches a boot that stalls without throwing (S3 3.1 D-SH19 / S4 18.1 D-DL19).
/// </summary>
/// <remarks>
/// The boot-diagnostics hand-off is unconditional on <c>Store.StartAsync</c> completing. If it
/// never does — a deadlock, an unbounded wait — the pending report sits in a local nobody reads and
/// the exe appears to do nothing and vanish. Fifteen seconds is tens of times a normal boot and a
/// little longer than the point a user starts to wonder.
/// </remarks>
internal sealed class BootWatchdog : IDisposable
{
    private readonly Action _onTimeout;
    private readonly ITimer _timer;
    private int _fired;
    private bool _disposed;

    /// <summary>Creates a disarmed watchdog.</summary>
    /// <param name="timeProvider">The clock, per S2 1.3.</param>
    /// <param name="onTimeout">Runs on a pool thread when the timeout passes.</param>
    internal BootWatchdog(TimeProvider timeProvider, Action onTimeout)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(onTimeout);

        _onTimeout = onTimeout;
        _timer = timeProvider.CreateTimer(_ => OnElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Starts the countdown. Called immediately before <c>host.Start()</c>.</summary>
    internal void Arm() => _timer.Change(ShellConstants.BootWatchdogTimeout, Timeout.InfiniteTimeSpan);

    /// <summary>Stops the countdown. Called right after <c>Store.StartAsync</c> completes.</summary>
    internal void Disarm() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

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
        if (Interlocked.Exchange(ref _fired, 1) == 0)
        {
            _onTimeout();
        }
    }
}
