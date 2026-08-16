using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using PoeOverlay.Composition;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Settings;
using PoeOverlay.Interop;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace PoeOverlay.Overlay;

/// <summary>
/// The always-on-top price surface: a raw layered Win32 parent with a WPF child (HLD 3.5 step 9 /
/// S3 4 / S4 12.6).
/// </summary>
/// <remarks>
/// <para>
/// There is no WPF <c>Window</c> here and there cannot be one. With <c>AllowsTransparency=false</c>
/// a WPF window silently refuses <c>WS_EX_LAYERED</c> — four different write points all returned
/// success with <c>GetLastError == 0</c> while <c>GWL_EXSTYLE</c> read back without the bit, and
/// <c>SetLayeredWindowAttributes</c> then failed with 87 (<c>00-shell-measurements.md</c> §11.1).
/// With <c>AllowsTransparency=true</c> ClearType dies for the whole window, measured at 0.0%
/// chromatic ink against 90.4% (§8.1), and nothing inside the window brings it back (§8.2). The
/// raw parent takes the bit immediately, its key and alpha apply to the child's pixels
/// indistinguishably from its own (§11.2), and ClearType in the child is bit-identical to a plain
/// opaque window (§11.3). That last row is the entire reason this structure exists.
/// </para>
/// <para>
/// What <c>Window</c> used to do and this type now does by hand: auto height (the child's
/// <c>WM_SIZE</c> drives a <c>SetWindowPos</c> on the parent, §11.6), dragging (manual capture plus
/// <c>SetWindowPos</c>, because <c>DragMove</c> is gone) and click-through (adding and removing
/// <c>WS_EX_TRANSPARENT</c> on the parent).
/// </para>
/// </remarks>
internal sealed class OverlayHost : IDisposable
{
    private readonly OverlayViewModel _viewModel;
    private readonly ExtendedStyleGate.Factory _gateFactory;
    private readonly LayeredHostWindowFactory _windowFactory;
    private readonly ISettingsSource _settings;
    private readonly ILogger<OverlayHost> _logger;

    private LayeredHostWindowHandle? _parent;
    private HwndSource? _source;
    private OverlayView? _view;
    private ExtendedStyleGate? _gate;

    private bool _isLayered;
    private bool _moveModeActive;
    private bool _gripResized;
    private bool _dragging;
    private bool _disposed;

    private NativeMethods.NativePoint _gestureOrigin;
    private Point _dragStart;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    /// <summary>Builds the host. No window exists until <see cref="Show"/>.</summary>
    /// <param name="viewModel">The display state. Attached to the fan-out by the composition root.</param>
    /// <param name="gateFactory">Deferred because the HWND does not exist until <see cref="Show"/>.</param>
    /// <param name="windowFactory">Creates the raw layered parent.</param>
    /// <param name="settings">Read for geometry and opacity; written only through the value-capture path.</param>
    /// <param name="logger">Records a refused layered configuration rather than leaving it silent.</param>
    internal OverlayHost(
        OverlayViewModel viewModel,
        ExtendedStyleGate.Factory gateFactory,
        LayeredHostWindowFactory windowFactory,
        ISettingsSource settings,
        ILogger<OverlayHost> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(gateFactory);
        ArgumentNullException.ThrowIfNull(windowFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _viewModel = viewModel;
        _gateFactory = gateFactory;
        _windowFactory = windowFactory;
        _settings = settings;
        _logger = logger;

        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised when a drag or resize releases capture, whatever the reason (S3 4.6.1).</summary>
    internal event EventHandler? CaptureReleased;

    /// <summary>Raised while a drag or resize is in progress, to kick the inactivity watchdog.</summary>
    internal event EventHandler? DragActivity;

    /// <summary>The parent HWND, or <see cref="IntPtr.Zero"/> before <see cref="Show"/>.</summary>
    /// <remarks>The settings window's owner (S3 5.1); see <c>SettingsWindowFactory</c>.</remarks>
    internal IntPtr Handle => _parent?.Hwnd ?? IntPtr.Zero;

    /// <summary>True while the layered configuration is actually in force.</summary>
    internal bool IsLayered => _isLayered;

    /// <summary>True while a mouse capture is outstanding anywhere in the hosted content.</summary>
    internal bool HasCapture => _view?.IsMouseCaptureWithin ?? false;

    /// <summary>True when the grip actually changed the height during this move-mode session.</summary>
    internal bool HeightWasGripped => _gripResized;

    /// <summary>The window's left edge in DIPs.</summary>
    internal double Left
    {
        get => ParentBoundsInDips().X;
        set => MoveParent(value, Top);
    }

    /// <summary>The window's top edge in DIPs.</summary>
    internal double Top
    {
        get => ParentBoundsInDips().Y;
        set => MoveParent(Left, value);
    }

    /// <summary>The content width in DIPs. Drives the child, and the parent follows it.</summary>
    internal double Width
    {
        get => _view?.Width ?? _settings.Current.Window.Width;
        set
        {
            if (_view is not null)
            {
                _view.Width = value;
            }
        }
    }

    /// <summary>The realised width in DIPs.</summary>
    internal double ActualWidth => _view?.ActualWidth ?? 0d;

    /// <summary>The realised height in DIPs.</summary>
    internal double ActualHeight => _view?.ActualHeight ?? 0d;

    /// <summary>
    /// Creates the parent, hosts the WPF child inside it and shows the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order matters. The layered attributes are applied before the window is ever shown, so a
    /// first frame at full opacity with a magenta background never reaches the screen; and the
    /// child is created before the geometry is applied, because the DPI transform the geometry
    /// needs comes from the child's composition target.
    /// </para>
    /// <para>
    /// <c>HwndSource.SizeToContent</c> resizes the child HWND and stops there — the parent stays at
    /// its old size, measured as 640×400 → 88×16 against a parent that never moved (§11.6). So the
    /// root visual carries explicit <c>Width</c>/<c>Height</c>/<c>MaxHeight</c>, WPF sizes the child
    /// HWND to that, and <see cref="OnChildMessage"/> forwards the resulting <c>WM_SIZE</c> to the
    /// parent. That is the half the measurement said had to be written by hand.
    /// </para>
    /// </remarks>
    internal void Show()
    {
        if (_parent is not null)
        {
            return;
        }

        var window = _settings.Current.Window;

        // Physical pixels are not yet convertible — the transform lives on a composition target
        // that does not exist. The window is created at the stored DIPs and corrected by
        // ApplySavedGeometry below, once the child can answer for the scale.
        var bounds = new NativeMethods.NativeRect
        {
            Left = (int)Math.Round(window.X),
            Top = (int)Math.Round(window.Y),
            Right = (int)Math.Round(window.X + window.Width),
            Bottom = (int)Math.Round(window.Y + Math.Max(window.Height, ShellConstants.FooterHeight)),
        };

        _parent = _windowFactory.Create(
            ShellConstants.OverlayWindowClassName,
            ShellConstants.OverlayWindowTitle,
            ShellConstants.ColorKeyRef,
            bounds);

        _gate = _gateFactory(_parent.Hwnd);
        ApplyLayeredAttributes();

        _view = new OverlayView(_viewModel);

        var parameters = new HwndSourceParameters(ShellConstants.OverlayContentWindowTitle)
        {
            ParentWindow = _parent.Hwnd,
            WindowStyle = unchecked((int)(Win32Constants.WsChild | Win32Constants.WsVisible | Win32Constants.WsClipSiblings)),
            UsesPerPixelOpacity = false,
            PositionX = 0,
            PositionY = 0,
            Width = bounds.Right - bounds.Left,
            Height = bounds.Bottom - bounds.Top,
        };

        _source = new HwndSource(parameters) { RootVisual = _view, SizeToContent = SizeToContent.WidthAndHeight };
        _source.AddHook(OnChildMessage);

        _view.Body.MouseLeftButtonDown += OnBodyPressed;
        _view.Body.MouseMove += OnBodyMoved;
        _view.Body.MouseLeftButtonUp += OnBodyReleased;
        _view.Body.LostMouseCapture += OnBodyCaptureLost;
        _view.ResizeGrip.MouseLeftButtonDown += OnGripPressed;
        _view.ResizeGrip.MouseMove += OnGripMoved;
        _view.ResizeGrip.MouseLeftButtonUp += OnGripReleased;
        _view.ResizeGrip.LostMouseCapture += OnGripCaptureLost;

        _view.Width = window.Width;
        _ = ApplySavedGeometry();
        ApplyHeightPolicy(moveModeActive: false);

        _parent.ShowNoActivate();
    }

    /// <summary>Turns click-through on. Used when leaving move mode.</summary>
    internal void EnableClickThrough() => _gate?.ApplyOr(ExtendedStyleBits.Transparent);

    /// <summary>Turns click-through off so the window can be dragged. <c>NOACTIVATE</c> stays set.</summary>
    internal void DisableClickThrough() => _gate?.ApplyAndNot(ExtendedStyleBits.Transparent);

    /// <summary>
    /// Shows or hides the inner border and the grip (S3 4.6.2 D-SH10).
    /// </summary>
    /// <param name="visible">True while move mode is active.</param>
    /// <remarks>
    /// The border is recoloured, not collapsed. It wraps the whole content, so collapsing it
    /// collapses the overlay — the shipped build did that and rendered as a 420×8 strip with
    /// nothing in it.
    /// </remarks>
    internal void ShowMoveModeAffordances(bool visible)
    {
        _moveModeActive = visible;
        _gripResized = false;

        if (_view is null)
        {
            return;
        }

        _view.MoveModeBorder.BorderBrush = visible ? OverlayView.MoveModeBrush : OverlayView.BodyBrush;
        _view.ResizeGrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Gives back any capture a gesture still holds (S3 4.6.1 D4-c).
    /// </summary>
    /// <remarks>
    /// Idempotent, and a no-op when nothing is captured. Leaving move mode hides the grip, and a
    /// hidden element goes on receiving mouse input while it holds the capture — so the mode's exit
    /// path drops the capture itself rather than trusting the gesture to have ended. Each release
    /// runs the normal <c>LostMouseCapture</c> path, which is where the commit lives.
    /// </remarks>
    internal void ReleaseGestureCapture()
    {
        if (_view is null)
        {
            return;
        }

        if (_view.ResizeGrip.IsMouseCaptured)
        {
            _view.ResizeGrip.ReleaseMouseCapture();
        }

        if (_view.Body.IsMouseCaptured)
        {
            _view.Body.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// Applies the height policy for the current mode (S3 4.4 D-SH7).
    /// </summary>
    /// <param name="moveModeActive">True while move mode is on, when the ratchet is lifted.</param>
    /// <remarks>
    /// <para>
    /// The <c>SizeToContent=Height</c> of the old window becomes <c>Height = NaN</c> on the root
    /// visual: WPF measures the content, sizes the child HWND to it, and the parent follows.
    /// </para>
    /// <para>
    /// Move mode lifts <c>MaxHeight</c> and touches <c>Height</c> not at all. Pinning it to the
    /// realised height on entry looks equivalent and is not: <c>ActualHeight</c> is layout-rounded,
    /// so the pinned value can be a fraction of a pixel short of the content, the last row stops
    /// fitting, the "+n more" marker appears, the marker's own height makes the shortfall worse and
    /// the count latches at one. Observed on screen — a single-item watchlist reading "+1 more"
    /// beside the row it was counting.
    /// </para>
    /// </remarks>
    internal void ApplyHeightPolicy(bool moveModeActive)
    {
        if (_view is null)
        {
            return;
        }

        var window = _settings.Current.Window;

        if (moveModeActive)
        {
            _view.MaxHeight = double.PositiveInfinity;
            return;
        }

        _view.MaxHeight = ComputeClipHeight();
        _view.Height = window.HeightMode == HeightMode.Explicit ? window.Height : double.NaN;
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
            MoveParent(window.X, window.Y);
            return true;
        }

        var (x, y) = OverlayGeometryValidator.ClampToDefault();
        MoveParent(x, y);
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
        var transform = _source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;

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
        var origin = ParentBoundsInDips();
        var current = _settings.Current;
        _settings.Update(current with { Window = current.Window with { X = origin.X, Y = origin.Y } });
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
        if (_view is null)
        {
            return;
        }

        _view.UpdateLayout();
        var current = _settings.Current;
        _settings.Update(current with
        {
            Window = current.Window with
            {
                Width = _view.ActualWidth,
                Height = _view.ActualHeight,
                HeightMode = HeightMode.Explicit,
            },
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;

        if (_view is not null)
        {
            _view.Body.MouseLeftButtonDown -= OnBodyPressed;
            _view.Body.MouseMove -= OnBodyMoved;
            _view.Body.MouseLeftButtonUp -= OnBodyReleased;
            _view.Body.LostMouseCapture -= OnBodyCaptureLost;
            _view.ResizeGrip.MouseLeftButtonDown -= OnGripPressed;
            _view.ResizeGrip.MouseMove -= OnGripMoved;
            _view.ResizeGrip.MouseLeftButtonUp -= OnGripReleased;
            _view.ResizeGrip.LostMouseCapture -= OnGripCaptureLost;
            _view.Detach();
        }

        if (_source is not null)
        {
            _source.RemoveHook(OnChildMessage);
            _source.Dispose();
        }

        _parent?.Dispose();
    }

    /// <summary>
    /// Applies the colour key and the window-wide alpha, and reports a refusal rather than hiding it.
    /// </summary>
    /// <remarks>
    /// One call is enough — the child redraws under the key and alpha without either being
    /// re-applied (<c>00-shell-measurements.md</c> §11.2, last row) — but the opacity setting
    /// changes the alpha, so this runs again on that change.
    /// </remarks>
    private void ApplyLayeredAttributes()
    {
        if (_gate is null)
        {
            return;
        }

        var style = _gate.Read();
        _isLayered = (style & ExtendedStyleBits.Layered) != 0;

        if (!_isLayered)
        {
            _logger.LogError(
                "The overlay parent is not layered (GWL_EXSTYLE read back as 0x{ExStyle:X8}). "
                + "Colour-key transparency and the opacity setting have no effect; the overlay is opaque.",
                (uint)style);
            return;
        }

        var alpha = (byte)Math.Round(Math.Clamp(_settings.Current.Window.Opacity, 0d, 1d) * 255d);
        if (!_gate.SetLayered(ShellConstants.ColorKeyRef, alpha, LwaFlags.ColorKey | LwaFlags.Alpha))
        {
            _isLayered = false;
            _logger.LogError(
                "SetLayeredWindowAttributes was refused for the overlay parent; the overlay is opaque "
                + "and the opacity setting has no effect.");
        }
    }

    /// <summary>
    /// Keeps the parent the same size as the child (<c>00-shell-measurements.md</c> §11.6).
    /// </summary>
    /// <remarks>
    /// This is the whole of the auto-height mechanism. Whatever changes the child's size — content
    /// growth, an explicit height, the grip — arrives here as one <c>WM_SIZE</c>, and the parent is
    /// moved to match. There is no feedback loop: resizing the parent does not resize the child.
    /// </remarks>
    private IntPtr OnChildMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32Constants.WmSize || _parent is null || _parent.Hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var (width, height) = DecodeSize(lParam);

        _ = NativeMethods.SetWindowPos(
            _parent.Hwnd,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            Win32Constants.SwpNoMove | Win32Constants.SwpNoZOrder | Win32Constants.SwpNoActivate | Win32Constants.SwpNoOwnerZOrder);

        return IntPtr.Zero;
    }

    /// <summary>
    /// Unpacks the client size <c>WM_SIZE</c> carries in <c>lParam</c>.
    /// </summary>
    /// <param name="lParam">The message's <c>lParam</c>: low word width, high word height.</param>
    /// <returns>The new client size in physical pixels.</returns>
    /// <remarks>
    /// Separated out because it is the one part of the parent-follows-child rule that can be tested
    /// without a desktop, and because sign extension of a 64-bit <c>IntPtr</c> makes a naive shift
    /// wrong for the high word.
    /// </remarks>
    internal static (int Width, int Height) DecodeSize(IntPtr lParam)
    {
        var packed = unchecked((uint)lParam.ToInt64());
        return ((int)(packed & 0xFFFF), (int)((packed >> 16) & 0xFFFF));
    }

    private double ComputeClipHeight()
    {
        var origin = ParentBoundsInDips();
        var width = Math.Max(Width, 1d);
        var footer = new Size(Math.Max(width, ShellConstants.FooterHeight), ShellConstants.FooterHeight);
        var bounds = new Rect(origin.X, origin.Y, width, Math.Max(ActualHeight, ShellConstants.FooterHeight));
        var area = OverlayGeometryValidator.BestWorkArea(bounds, GetWorkAreas(), footer) ?? SystemParameters.WorkArea;
        var available = area.Bottom - origin.Y;
        return available <= ShellConstants.FooterHeight ? ShellConstants.FooterHeight : available;
    }

    private Point ParentBoundsInDips()
    {
        if (_parent is null || _parent.Hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(_parent.Hwnd, out var rect))
        {
            var window = _settings.Current.Window;
            return new Point(window.X, window.Y);
        }

        var transform = _source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        return transform.Transform(new Point(rect.Left, rect.Top));
    }

    private void MoveParent(double xDip, double yDip)
    {
        if (_parent is null || _parent.Hwnd == IntPtr.Zero)
        {
            return;
        }

        var transform = _source?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        var device = transform.Transform(new Point(xDip, yDip));
        MoveParentToDevice((int)Math.Round(device.X), (int)Math.Round(device.Y));
    }

    private void MoveParentToDevice(int x, int y)
    {
        if (_parent is null || _parent.Hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            _parent.Hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            Win32Constants.SwpNoSize | Win32Constants.SwpNoZOrder | Win32Constants.SwpNoActivate | Win32Constants.SwpNoOwnerZOrder);
    }

    private void OnSettingsChanged(AppSettings oldSettings, AppSettings newSettings)
    {
        var dispatcher = _view?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, () => OnSettingsChanged(oldSettings, newSettings));
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

    /// <summary>
    /// Begins a manual drag. <c>DragMove</c> does not exist without a <c>Window</c>.
    /// </summary>
    /// <remarks>
    /// Unlike <c>DragMove</c>, which blocked until the gesture ended, this returns immediately and
    /// the gesture finishes in <see cref="OnBodyCaptureLost"/> — so the commit and the
    /// <see cref="CaptureReleased"/> notification move there too, and the capture invariant of
    /// D4-c is expressed by the same <c>LostMouseCapture</c> path the grip already used.
    /// </remarks>
    private void OnBodyPressed(object sender, MouseButtonEventArgs e)
    {
        if (!_moveModeActive || _view is null || ReferenceEquals(e.OriginalSource, _view.ResizeGrip))
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out _gestureOrigin))
        {
            return;
        }

        _dragStart = ParentBoundsInDips();
        _dragging = _view.Body.CaptureMouse();
        DragActivity?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnBodyMoved(object sender, MouseEventArgs e)
    {
        if (!_dragging || !NativeMethods.GetCursorPos(out var now))
        {
            return;
        }

        var transform = _source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var delta = transform.Transform(new Point(now.X - _gestureOrigin.X, now.Y - _gestureOrigin.Y));
        MoveParent(_dragStart.X + delta.X, _dragStart.Y + delta.Y);
        DragActivity?.Invoke(this, EventArgs.Empty);
    }

    private void OnBodyReleased(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _view?.Body.ReleaseMouseCapture();
        }
    }

    private void OnBodyCaptureLost(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            CommitPosition();
        }

        CaptureReleased?.Invoke(this, EventArgs.Empty);
    }

    private void OnGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (!_moveModeActive || _view is null || !NativeMethods.GetCursorPos(out _gestureOrigin))
        {
            return;
        }

        _resizeStartWidth = _view.ActualWidth;
        _resizeStartHeight = _view.ActualHeight;
        _ = _view.ResizeGrip.CaptureMouse();
        DragActivity?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>
    /// Ends the grip gesture on the button release.
    /// </summary>
    /// <remarks>
    /// Without this the capture is never given back. WPF keeps routing <c>MouseMove</c> to the
    /// captured element whether or not a button is down, so after one press-and-release the overlay
    /// went on resizing itself to follow the bare cursor until move mode was left — observed on
    /// screen with the button state read directly and the window rect tracking the cursor. The body
    /// drag has always had this handler; the grip did not.
    /// </remarks>
    private void OnGripReleased(object sender, MouseButtonEventArgs e)
    {
        if (_view is null || !_view.ResizeGrip.IsMouseCaptured)
        {
            return;
        }

        // The commit and the CaptureReleased notification both live in OnGripCaptureLost, which
        // this call reaches synchronously — the same single exit point the body drag uses.
        _view.ResizeGrip.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnGripMoved(object sender, MouseEventArgs e)
    {
        if (_view is null || !_view.ResizeGrip.IsMouseCaptured || !NativeMethods.GetCursorPos(out var now))
        {
            return;
        }

        var transform = _source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var delta = transform.Transform(new Point(now.X - _gestureOrigin.X, now.Y - _gestureOrigin.Y));

        _view.Width = Math.Max(SettingsValidation.MinWindowExtent, _resizeStartWidth + delta.X);
        _view.Height = Math.Max(ShellConstants.FooterHeight * 2d, _resizeStartHeight + delta.Y);
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
}
