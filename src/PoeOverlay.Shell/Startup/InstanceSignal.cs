using Microsoft.Extensions.Logging;
using PoeOverlay.Composition;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Interop;

namespace PoeOverlay.Startup;

/// <summary>How <see cref="InstanceSignal.TrySend"/> ended.</summary>
internal enum InstanceSignalSendResult
{
    /// <summary>The receiver replied with the sentinel; the handler really ran.</summary>
    Acknowledged,

    /// <summary>Every attempt timed out, or returned success without the sentinel.</summary>
    NoResponse,

    /// <summary>No receiver window exists under <c>HWND_MESSAGE</c>.</summary>
    WindowNotFound,
}

/// <summary>
/// The single-instance channel: a message-only receiver, and a static sender (S3 3.2 / S4 12.5).
/// </summary>
/// <remarks>
/// The handler is also the reachability fallback (S3 3.2 D-SH14). Once the tray has failed
/// permanently and the user has dismissed the banner, launching the exe again is the only route
/// left to the process, and it is a route the user reaches for naturally.
/// </remarks>
internal sealed class InstanceSignal : IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly MessageOnlyWindowFactory _windowFactory;
    private readonly Action _onSignalReceived;
    private readonly ILogger<InstanceSignal> _logger;

    private MessageOnlyWindowHandle? _window;
    private uint _messageId;
    private volatile bool _receiving;
    private bool _disposed;

    /// <summary>Wires the receiving half.</summary>
    /// <param name="dispatcher">Used only to check thread affinity and to marshal a stray call.</param>
    /// <param name="windowFactory">Creates the <c>HWND_MESSAGE</c> window.</param>
    /// <param name="onSignalReceived">Shows and activates the settings window (D-SH14).</param>
    /// <param name="logger">Diagnostics.</param>
    internal InstanceSignal(
        IUiDispatcher dispatcher,
        MessageOnlyWindowFactory windowFactory,
        Action onSignalReceived,
        ILogger<InstanceSignal> logger)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(windowFactory);
        ArgumentNullException.ThrowIfNull(onSignalReceived);
        ArgumentNullException.ThrowIfNull(logger);

        _dispatcher = dispatcher;
        _windowFactory = windowFactory;
        _onSignalReceived = onSignalReceived;
        _logger = logger;
    }

    /// <summary>The receiver handle, for tests. <see cref="IntPtr.Zero"/> before <see cref="StartReceiving"/>.</summary>
    internal IntPtr Hwnd => _window?.Hwnd ?? IntPtr.Zero;

    /// <summary>Creates the receiver window and begins answering signals.</summary>
    internal void StartReceiving()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is not null)
        {
            return;
        }

        _messageId = NativeMethods.RegisterWindowMessageW(ShellConstants.SignalMessageName);
        _window = _windowFactory.Create(
            ShellConstants.SignalWindowClassName,
            ShellConstants.SignalWindowTitle,
            OnMessage);
        _receiving = true;
    }

    /// <summary>
    /// Stops answering signals without destroying the window.
    /// </summary>
    /// <remarks>
    /// Called in teardown step a, strictly before the mutex is released in step d. Reversing the
    /// two lets a relaunch reach a process that is already dismantling itself (S3 3.3).
    /// </remarks>
    internal void StopReceiving() => _receiving = false;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _receiving = false;
        _window?.Dispose();
        _window = null;
    }

    /// <summary>
    /// Signals the first instance and judges the reply by the sentinel, not the return value.
    /// </summary>
    /// <param name="className">The receiver's window class (A4 — there is no way to learn its PID).</param>
    /// <param name="perAttemptTimeout">Timeout of one <c>SendMessageTimeout</c>.</param>
    /// <param name="maxAttempts">How many times to send before giving up.</param>
    /// <returns>What happened.</returns>
    internal static InstanceSignalSendResult TrySend(string className, TimeSpan perAttemptTimeout, int maxAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        var messageId = NativeMethods.RegisterWindowMessageW(ShellConstants.SignalMessageName);
        if (messageId == 0)
        {
            return InstanceSignalSendResult.NoResponse;
        }

        var target = FindReceiver(className);
        if (target == IntPtr.Zero)
        {
            return InstanceSignalSendResult.WindowNotFound;
        }

        // Handing foreground rights to the other process is the documented use of this API;
        // applying it to oneself does nothing at all (measured 1.4).
        _ = NativeMethods.AllowSetForegroundWindow(Win32Constants.AsfwAny);

        var timeoutMs = (uint)Math.Max(1, perAttemptTimeout.TotalMilliseconds);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var returned = NativeMethods.SendMessageTimeoutW(
                target,
                messageId,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32Constants.SmtoAbortIfHung,
                timeoutMs,
                out var result);

            if (returned != IntPtr.Zero && (int)result == ShellConstants.AckSentinel)
            {
                return InstanceSignalSendResult.Acknowledged;
            }

            // A success return with the wrong sentinel is indistinguishable from silence, and the
            // measurement showed it is produced by DestroyWindow unblocking a pending send.
            if (attempt < maxAttempts)
            {
                Thread.Sleep(ShellConstants.FindWindowRetrySpacing);
            }
        }

        return InstanceSignalSendResult.NoResponse;
    }

    private static IntPtr FindReceiver(string className)
    {
        for (var attempt = 1; attempt <= ShellConstants.FindWindowAttempts; attempt++)
        {
            // HWND_BROADCAST never reaches a message-only window, so the parent is not optional.
            var hwnd = NativeMethods.FindWindowExW(Win32Constants.HwndMessage, IntPtr.Zero, className, null);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }

            if (attempt < ShellConstants.FindWindowAttempts)
            {
                Thread.Sleep(ShellConstants.FindWindowRetrySpacing);
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr? OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != _messageId || _messageId == 0)
        {
            return null;
        }

        if (!_receiving)
        {
            // Deliberately not the sentinel: reception is off, so nothing ran.
            return IntPtr.Zero;
        }

        // The window procedure runs on the thread that pumps, which is the UI thread. The branch
        // below exists so that a future change to that arrangement fails loudly rather than
        // silently touching WPF objects from elsewhere.
        if (_dispatcher.CheckAccess())
        {
            RunHandler();
        }
        else
        {
            _logger.LogWarning("Instance signal arrived off the UI thread; marshalling.");
            _dispatcher.Post(RunHandler);
        }

        return ShellConstants.AckSentinel;
    }

    private void RunHandler()
    {
#pragma warning disable CA1031 // The reachability fallback must never take the process down with it.
        try
        {
            _onSignalReceived();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Instance signal handler failed.");
        }
#pragma warning restore CA1031
    }
}
