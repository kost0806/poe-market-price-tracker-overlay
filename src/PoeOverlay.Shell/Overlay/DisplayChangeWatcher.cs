using Microsoft.Win32;
using PoeOverlay.Core.Presentation.Fanout;

namespace PoeOverlay.Overlay;

/// <summary>
/// Re-runs the geometry check when the desktop layout changes (HLD D22 / S3 4.7 D-SH11).
/// </summary>
/// <remarks>
/// <c>SystemEvents</c> raises on its own thread, so the event is marshalled through
/// <see cref="IUiDispatcher"/> rather than a captured <c>Dispatcher</c> — <c>Startup/</c> hides
/// behind the same abstraction <c>Interop/</c> does (S3 4.7 M3).
/// </remarks>
internal sealed class DisplayChangeWatcher : IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly OverlayModeService _modeService;
    private bool _disposed;

    /// <summary>Subscribes to display changes.</summary>
    /// <param name="dispatcher">Marshals the callback to the UI thread.</param>
    /// <param name="modeService">Owns the deferral rule while move mode is active.</param>
    internal DisplayChangeWatcher(IUiDispatcher dispatcher, OverlayModeService modeService)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(modeService);

        _dispatcher = dispatcher;
        _modeService = modeService;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => _dispatcher.Post(_modeService.RequestRevalidation);
}
