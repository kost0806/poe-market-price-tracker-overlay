using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Overlay;

/// <summary>
/// The settings window's only route to overlay geometry (S3 4.3.1 D-PS7 / S4 12.3).
/// </summary>
/// <remarks>
/// Both commands compute values and queue them through the same value-capture path the drag
/// handlers use, so the Shell remains the single writer of <c>x/y/width/height/heightMode</c>. The
/// alternative — the settings view model writing settings itself — breaks the invariant D19's
/// value-capture queueing rests on.
/// </remarks>
internal sealed class OverlayGeometryService : IOverlayGeometryService
{
    private readonly OverlayHost _window;
    private readonly ISettingsSource _settings;

    /// <summary>Wires the service.</summary>
    /// <param name="window">The overlay, so the new geometry takes effect immediately.</param>
    /// <param name="settings">Where the values are queued.</param>
    internal OverlayGeometryService(OverlayHost window, ISettingsSource settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(settings);

        _window = window;
        _settings = settings;
    }

    /// <inheritdoc />
    public void ResetPlacement()
    {
        var current = _settings.Current;
        var defaults = WindowSettings.Default;

        _settings.Update(current with
        {
            Window = current.Window with
            {
                X = defaults.X,
                Y = defaults.Y,
                Width = defaults.Width,
                Height = defaults.Height,
                HeightMode = HeightMode.Auto,
            },
        });

        _window.Left = defaults.X;
        _window.Top = defaults.Y;
        _window.Width = defaults.Width;
        _window.ApplyHeightPolicy(moveModeActive: false);
    }

    /// <inheritdoc />
    public void RevertHeightToAuto()
    {
        var current = _settings.Current;
        if (current.Window.HeightMode == HeightMode.Auto)
        {
            return;
        }

        _settings.Update(current with { Window = current.Window with { HeightMode = HeightMode.Auto } });
        _window.ApplyHeightPolicy(moveModeActive: false);
    }
}
