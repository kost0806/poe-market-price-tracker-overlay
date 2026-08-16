namespace PoeOverlay.Startup;

/// <summary>
/// The named mutex that makes a second launch a signal rather than a second poller (S4 12.5).
/// </summary>
/// <remarks>
/// Released before <c>host.StopAsync</c>, never after: a stop may take the full five seconds, and a
/// relaunch during that window must be able to take the mutex rather than fall through to the
/// signal path, where nothing is left alive to answer (HLD 3.5 12-d).
/// </remarks>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _held;
    private bool _disposed;

    /// <summary>Creates an unacquired guard over <paramref name="mutexName"/>.</summary>
    /// <param name="mutexName">A system-wide mutex name.</param>
    internal SingleInstanceGuard(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        _mutex = new Mutex(false, mutexName);
    }

    /// <summary>True while this process owns the mutex.</summary>
    internal bool IsHeld => _held;

    /// <summary>Tries to take ownership without waiting.</summary>
    /// <returns>True when this process is the first instance.</returns>
    internal bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _held = _mutex.WaitOne(TimeSpan.Zero, false);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died without releasing. Ownership transfers to us regardless, and
            // a crashed predecessor is precisely the case where this process must carry on.
            _held = true;
        }

        return _held;
    }

    /// <summary>Releases ownership if held. Idempotent.</summary>
    internal void Release()
    {
        if (!_held)
        {
            return;
        }

        _held = false;
        _mutex.ReleaseMutex();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();
        _mutex.Dispose();
    }
}
