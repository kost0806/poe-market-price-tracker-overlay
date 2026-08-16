using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using PoeOverlay.Composition;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Settings;
using Microsoft.Extensions.Logging;
using PoeOverlay.Interop;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;

namespace PoeOverlay.Overlay;

/// <summary>
/// The always-on-top price surface (HLD 3.5 step 9 / S3 4 / S4 12.6).
/// </summary>
/// <remarks>
/// <para>
/// <c>AllowsTransparency</c> is <see langword="false"/> and stays false. Turning it on disables
/// ClearType for the whole window — measured at 0.0% chromatic ink pixels against 90.4% for a
/// normal window — and nothing inside the window brings it back: an opaque background, an opaque
/// <c>Border</c> under the text and an explicit <c>TextRenderingMode</c> all measured 0.00%
/// (<c>00-shell-measurements.md</c> §8.1, §8.2). This application exists to read small digits, so
/// per-pixel alpha was traded for the three-times horizontal sampling ClearType carries in the
/// colour channels. The cost is real and permanent: no antialiased rounded corners, no soft
/// shadows, one uniform alpha for the whole window, and hard binary colour-key edges.
/// </para>
/// <para>
/// Consequently <c>Background="Transparent"</c> is a trap here — under
/// <c>AllowsTransparency=false</c> it renders as opaque black, not see-through (measured
/// RGB (0,0,0)). The background is therefore either the colour key or a real opaque colour, never
/// "Transparent"; which of the two it ends up being is decided at runtime by
/// <see cref="OnContentRendered"/>, and the XAML starts from the opaque one so a refused layered
/// configuration does not leave a magenta rectangle on screen.
/// </para>
/// </remarks>
public sealed partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly ExtendedStyleGate.Factory _gateFactory;
    private readonly ISettingsSource _settings;
    private readonly ILogger<OverlayWindow> _logger;

    private ExtendedStyleGate? _gate;
    private bool _isLayered;
    private ClippingRowsPanel? _rowsPanel;
    private bool _moveModeActive;
    private bool _gripResized;
    private Point _resizeOrigin;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    /// <summary>Builds the overlay.</summary>
    /// <param name="viewModel">The display state. Attached to the fan-out by the composition root.</param>
    /// <param name="gateFactory">Deferred because the HWND does not exist until <c>SourceInitialized</c>.</param>
    /// <param name="settings">Read for geometry and opacity; written only through the value-capture path.</param>
    /// <param name="logger">Records a refused layered configuration rather than leaving it silent.</param>
    public OverlayWindow(
        OverlayViewModel viewModel,
        ExtendedStyleGate.Factory gateFactory,
        ISettingsSource settings,
        ILogger<OverlayWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(gateFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _viewModel = viewModel;
        _gateFactory = gateFactory;
        _settings = settings;
        _logger = logger;

        InitializeComponent();
        DataContext = viewModel;

        _settings.Changed += OnSettingsChanged;
        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
        Closed += OnClosed;
        ResizeGrip.MouseLeftButtonDown += OnGripPressed;
        ResizeGrip.MouseMove += OnGripMoved;
        ResizeGrip.LostMouseCapture += OnGripCaptureLost;
        Body.MouseLeftButtonDown += OnBodyPressed;
    }

    /// <summary>Raised when a drag or resize releases capture, whatever the reason (S3 4.6.1).</summary>
    internal event EventHandler? CaptureReleased;

    /// <summary>Raised while a drag or resize is in progress, to kick the inactivity watchdog.</summary>
    internal event EventHandler? DragActivity;

    /// <summary>True while a mouse capture is outstanding anywhere in this window.</summary>
    internal bool HasCapture => IsMouseCaptureWithin;

    /// <summary>Called by <see cref="ClippingRowsPanel"/> once it is in the tree.</summary>
    /// <param name="panel">The panel that lays the rows out.</param>
    internal void AttachRowsPanel(ClippingRowsPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (ReferenceEquals(_rowsPanel, panel))
        {
            return;
        }

        if (_rowsPanel is not null)
        {
            _rowsPanel.HiddenCountChanged -= OnHiddenCountChanged;
        }

        _rowsPanel = panel;
        _rowsPanel.ReservedHeight = MoreRows.ActualHeight;
        _rowsPanel.HiddenCountChanged += OnHiddenCountChanged;
    }

    /// <summary>Turns click-through on. Used when leaving move mode.</summary>
    internal void EnableClickThrough() => _gate?.ApplyOr(ExtendedStyleBits.Transparent);

    /// <summary>Turns click-through off so the window can be dragged. <c>NOACTIVATE</c> stays set.</summary>
    internal void DisableClickThrough() => _gate?.ApplyAndNot(ExtendedStyleBits.Transparent);

    /// <summary>Shows or hides the inner border and the grip.</summary>
    /// <param name="visible">True while move mode is active.</param>
    internal void ShowMoveModeAffordances(bool visible)
    {
        _moveModeActive = visible;
        _gripResized = false;
        MoveModeBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ResizeGrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>True when the grip actually changed the height during this move-mode session.</summary>
    internal bool HeightWasGripped => _gripResized;

    /// <summary>
    /// Applies the height policy for the current mode (S3 4.4 D-SH7).
    /// </summary>
    /// <param name="moveModeActive">True while move mode is on, when the ratchet is lifted.</param>
    /// <remarks>
    /// Growth leaves exactly one unpainted band the size of the new row for one to three captured
    /// frames; shrinking leaves none. <c>SizeToContent=Height</c> measured better than assigning
    /// <c>Height</c> (one frame against two or three), which is why it is the adopted form
    /// (<c>00-shell-measurements.md</c> §9).
    /// </remarks>
    internal void ApplyHeightPolicy(bool moveModeActive)
    {
        var window = _settings.Current.Window;

        if (moveModeActive)
        {
            SizeToContent = SizeToContent.Manual;
            MaxHeight = double.PositiveInfinity;
            return;
        }

        MaxHeight = ComputeClipHeight();

        if (window.HeightMode == HeightMode.Explicit)
        {
            SizeToContent = SizeToContent.Manual;
            Height = window.Height;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
        }
    }

    /// <summary>
    /// Validates the stored geometry and applies it, or falls back to the default position.
    /// </summary>
    /// <returns>True when the stored position was kept.</returns>
    internal bool ApplySavedGeometry()
    {
        var window = _settings.Current.Window;
        Width = window.Width;

        var bounds = new Rect(window.X, window.Y, window.Width, Math.Max(window.Height, ShellConstants.FooterHeight));
        var footer = new Size(window.Width, ShellConstants.FooterHeight);

        if (OverlayGeometryValidator.HasMinimumVisibleArea(bounds, GetWorkAreas(), footer))
        {
            Left = window.X;
            Top = window.Y;
            return true;
        }

        var (x, y) = OverlayGeometryValidator.ClampToDefault();
        Left = x;
        Top = y;
        CommitPosition();
        return false;
    }

    /// <summary>Re-runs the geometry check, used by both move-mode entry and display changes (D-SH11).</summary>
    /// <returns>True when the stored position survived the check.</returns>
    internal bool Revalidate() => ApplySavedGeometry();

    /// <summary>Work areas of every screen, in DIPs.</summary>
    /// <returns>One rectangle per screen.</returns>
    internal IReadOnlyList<Rect> GetWorkAreas()
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;

        var areas = new List<Rect>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var device = screen.WorkingArea;
            var topLeft = transform.Transform(new Point(device.Left, device.Top));
            var bottomRight = transform.Transform(new Point(device.Right, device.Bottom));
            areas.Add(new Rect(topLeft, bottomRight));
        }

        if (areas.Count == 0)
        {
            areas.Add(SystemParameters.WorkArea);
        }

        return areas;
    }

    /// <summary>Stores the current position (S3 4.3, value capture only).</summary>
    internal void CommitPosition()
    {
        var current = _settings.Current;
        _settings.Update(current with { Window = current.Window with { X = Left, Y = Top } });
    }

    /// <summary>
    /// Stores the current size, switching the height policy to explicit (S3 4.3).
    /// </summary>
    /// <remarks>
    /// <c>ActualHeight</c> rather than <c>Height</c>: within one dispatcher pass, two
    /// <c>MaxHeight</c> writes without an intervening layout leave <c>Height</c> frozen on the
    /// first value while <c>ActualHeight</c> tracks the content (S3 4.3 R3). The correction costs
    /// nothing, so it is taken even though no concrete path to the collision is confirmed.
    /// </remarks>
    internal void CommitSize()
    {
        UpdateLayout();
        var current = _settings.Current;
        _settings.Update(current with
        {
            Window = current.Window with
            {
                Width = ActualWidth,
                Height = ActualHeight,
                HeightMode = HeightMode.Explicit,
            },
        });
    }

    private double ComputeClipHeight()
    {
        var footer = new Size(Math.Max(Width, ShellConstants.FooterHeight), ShellConstants.FooterHeight);
        var bounds = new Rect(Left, Top, Math.Max(Width, 1d), Math.Max(ActualHeight, ShellConstants.FooterHeight));
        var area = OverlayGeometryValidator.BestWorkArea(bounds, GetWorkAreas(), footer) ?? SystemParameters.WorkArea;
        var available = area.Bottom - Top;
        return available <= ShellConstants.FooterHeight ? ShellConstants.FooterHeight : available;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _gate = _gateFactory(hwnd);

        // One read-modify-write, through the gate. A wholesale assignment here is the accident
        // HLD D4-d exists to prevent, and SWP_FRAMECHANGED is deliberately absent.
        //
        // Click-through and no-activate go on now, before the window is ever shown; the layered bit
        // cannot (see OnContentRendered).
        _gate.ApplyOr(ExtendedStyleBits.Transparent | ExtendedStyleBits.NoActivate);

        _ = ApplySavedGeometry();
        ApplyHeightPolicy(moveModeActive: false);
    }

    /// <summary>
    /// Tries to make the window layered, and reports honestly when the platform refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>00-shell-measurements.md</c> §8.1 records <c>AllowsTransparency=false</c> +
    /// <c>WS_EX_LAYERED</c> + <c>SetLayeredWindowAttributes</c> as a working configuration, and §9
    /// records a live overlay at <c>GWL_EXSTYLE = 0x08080028</c>. On this runtime it is not
    /// reachable from a WPF <c>Window</c> at all. Probed four ways — at
    /// <c>SourceInitialized</c>, after <c>Show()</c>, at <c>ContentRendered</c>, and on a manually
    /// built <c>HwndSource</c> created with the bit already in <c>ExtendedWindowStyle</c> — every
    /// attempt returned success with <c>GetLastError == 0</c> and read back <c>0x08000028</c>, the
    /// layered bit filtered out; <c>SetLayeredWindowAttributes</c> then failed with
    /// <c>ERROR_INVALID_PARAMETER</c> (87). A stock WPF window and a bare
    /// <c>WindowStyle=None</c> window behaved identically, while a raw Win32 <c>STATIC</c> popup in
    /// the same process accepted the bit immediately. WPF's render target owns that flag whenever
    /// per-pixel opacity is off.
    /// </para>
    /// <para>
    /// So the attempt is made and the result is checked rather than assumed. When it succeeds the
    /// background becomes the colour key and the opacity slider drives <c>LWA_ALPHA</c>; when it
    /// does not, the overlay stays an opaque rectangle and the failure is recorded as an error
    /// rather than leaving a settings control that quietly does nothing.
    /// </para>
    /// </remarks>
    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (_gate is null)
        {
            return;
        }

        _gate.ApplyOr(ExtendedStyleBits.Layered);
        _isLayered = (_gate.Read() & ExtendedStyleBits.Layered) != 0;

        if (!_isLayered)
        {
            _logger.LogError(
                "The overlay window could not be made layered (GWL_EXSTYLE read back as 0x{ExStyle:X8}). "
                + "Colour-key transparency and the opacity setting have no effect; the overlay is opaque.",
                (uint)_gate.Read());
            return;
        }

        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0xFF));
        ApplyLayeredAttributes();
    }

    private void ApplyLayeredAttributes()
    {
        if (!_isLayered)
        {
            return;
        }

        var alpha = (byte)Math.Round(Math.Clamp(_settings.Current.Window.Opacity, 0d, 1d) * 255d);
        _gate?.SetLayered(ShellConstants.ColorKeyRef, alpha, LwaFlags.ColorKey | LwaFlags.Alpha);
    }

    private void OnSettingsChanged(AppSettings oldSettings, AppSettings newSettings)
    {
        if (!CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => OnSettingsChanged(oldSettings, newSettings));
            return;
        }

        if (oldSettings.Window.Opacity != newSettings.Window.Opacity)
        {
            ApplyLayeredAttributes();
        }

        if (oldSettings.Window.Width != newSettings.Window.Width)
        {
            Width = newSettings.Window.Width;
        }

        if (oldSettings.Window.HeightMode != newSettings.Window.HeightMode
            || oldSettings.Window.Height != newSettings.Window.Height)
        {
            ApplyHeightPolicy(_moveModeActive);
        }
    }

    private void OnHiddenCountChanged(object? sender, EventArgs e)
    {
        // Never inline: this fires from inside a measure pass, and the view model's reaction
        // rewrites a bound string.
        var count = _rowsPanel?.HiddenCount ?? 0;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _viewModel.HiddenRowCount = count);
    }

    private void OnBodyPressed(object sender, MouseButtonEventArgs e)
    {
        if (!_moveModeActive || e.OriginalSource == ResizeGrip)
        {
            return;
        }

        DragActivity?.Invoke(this, EventArgs.Empty);
        DragMove();

        // DragMove blocks until the drag ends, so this is the end of the gesture.
        CommitPosition();
        CaptureReleased?.Invoke(this, EventArgs.Empty);
    }

    private void OnGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (!_moveModeActive)
        {
            return;
        }

        _resizeOrigin = PointToScreen(e.GetPosition(this));
        _resizeStartWidth = ActualWidth;
        _resizeStartHeight = ActualHeight;
        _ = ResizeGrip.CaptureMouse();
        DragActivity?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnGripMoved(object sender, MouseEventArgs e)
    {
        if (!ResizeGrip.IsMouseCaptured)
        {
            return;
        }

        var now = PointToScreen(e.GetPosition(this));
        Width = Math.Max(240d, _resizeStartWidth + (now.X - _resizeOrigin.X));
        Height = Math.Max(ShellConstants.FooterHeight * 2d, _resizeStartHeight + (now.Y - _resizeOrigin.Y));
        _gripResized = true;
        DragActivity?.Invoke(this, EventArgs.Empty);
    }

    private void OnGripCaptureLost(object sender, MouseEventArgs e)
    {
        if (_gripResized)
        {
            CommitSize();
        }

        CaptureReleased?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        if (_rowsPanel is not null)
        {
            _rowsPanel.HiddenCountChanged -= OnHiddenCountChanged;
        }
    }
}
