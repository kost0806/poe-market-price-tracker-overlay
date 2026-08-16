using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Overlay;

/// <summary>
/// Move mode's state machine (S3 4.6 D-SH9 / S4 12.3).
/// </summary>
/// <remarks>
/// The ordering rule lives here and nowhere else: entering revalidates geometry, lifts the height
/// ratchet, drops <c>WS_EX_TRANSPARENT</c> (never <c>WS_EX_NOACTIVATE</c>) and shows the
/// affordances; leaving reverses it. A view model asks for the transition and never learns the
/// sequence (HLD D4-b).
/// </remarks>
internal sealed class OverlayModeService : IOverlayModeService, IDisposable
{
    private readonly OverlayHost _window;
    private readonly ISettingsSource _settings;
    private readonly MoveModeWatchdog _watchdog;
    private readonly ILogger<OverlayModeService> _logger;

    private bool _isActive;
    private bool _expiredWhileCaptured;
    private bool _revalidationPending;
    private bool _disposed;

    /// <summary>Wires the service to its window and builds the watchdog it owns.</summary>
    /// <param name="window">The overlay.</param>
    /// <param name="dispatcher">Where the watchdog marshals its timeout.</param>
    /// <param name="timeProvider">The clock, per S2 1.3.</param>
    /// <param name="settings">Read to restore the height policy on exit.</param>
    /// <param name="logger">Diagnostics.</param>
    internal OverlayModeService(
        OverlayHost window,
        IUiDispatcher dispatcher,
        TimeProvider timeProvider,
        ISettingsSource settings,
        ILogger<OverlayModeService> logger)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _window = window;
        _settings = settings;
        _logger = logger;
        _watchdog = new MoveModeWatchdog(dispatcher, timeProvider, OnIdleTimeout);

        _window.DragActivity += OnDragActivity;
        _window.CaptureReleased += OnCaptureReleased;
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public bool IsActive => _isActive;

    /// <inheritdoc />
    public void EnterMoveMode()
    {
        if (_isActive)
        {
            return;
        }

        _ = _window.Revalidate();
        _window.ApplyHeightPolicy(moveModeActive: true);
        _window.DisableClickThrough();
        _window.ShowMoveModeAffordances(visible: true);

        _expiredWhileCaptured = false;
        _isActive = true;
        _watchdog.Kick();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ExitMoveMode(MoveModeExitReason reason)
    {
        if (!_isActive)
        {
            return;
        }

        _logger.LogInformation("Move mode off ({Reason}).", reason);

        var gripped = _window.HeightWasGripped;
        _window.ShowMoveModeAffordances(visible: false);

        if (gripped)
        {
            // The grip committed an explicit height; the mode table's Explicit row applies.
            _window.CommitSize();
        }

        _window.ApplyHeightPolicy(moveModeActive: false);
        _window.EnableClickThrough();

        _watchdog.Stop();
        _expiredWhileCaptured = false;
        _isActive = false;
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (_revalidationPending)
        {
            _revalidationPending = false;
            _ = _window.Revalidate();
        }
    }

    /// <summary>
    /// Runs the geometry recheck, or defers it while a drag is in progress (S3 4.7 N8-①).
    /// </summary>
    /// <remarks>
    /// Snapping the window back to its default position while the user is holding it would throw it
    /// out from under the cursor — the same class of problem the watchdog defers for.
    /// </remarks>
    internal void RequestRevalidation()
    {
        if (_isActive)
        {
            _revalidationPending = true;
            return;
        }

        _ = _window.Revalidate();
        _window.ApplyHeightPolicy(moveModeActive: false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.DragActivity -= OnDragActivity;
        _window.CaptureReleased -= OnCaptureReleased;
        _watchdog.Dispose();
    }

    private void OnDragActivity(object? sender, EventArgs e)
    {
        if (_isActive && !_expiredWhileCaptured)
        {
            _watchdog.Kick();
        }
    }

    private void OnCaptureReleased(object? sender, EventArgs e)
    {
        if (!_isActive)
        {
            return;
        }

        if (_expiredWhileCaptured)
        {
            _expiredWhileCaptured = false;
            ExitMoveMode(MoveModeExitReason.WatchdogTimeout);
            return;
        }

        _watchdog.Kick();
    }

    private void OnIdleTimeout()
    {
        if (!_isActive)
        {
            return;
        }

        if (_window.HasCapture)
        {
            // Leaving now would break the capture invariant of D4-c. Once the flag is up the timer
            // is not restarted, so the exit cannot be postponed indefinitely.
            _expiredWhileCaptured = true;
            return;
        }

        ExitMoveMode(MoveModeExitReason.WatchdogTimeout);
    }

    /// <summary>The height policy the settings currently ask for; kept for readability of the table.</summary>
    internal HeightMode CurrentHeightMode => _settings.Current.Window.HeightMode;
}
