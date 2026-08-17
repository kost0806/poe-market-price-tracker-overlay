using System.Windows.Threading;
using PoeOverlay.Core.Presentation.ViewModels;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using UserControl = System.Windows.Controls.UserControl;

namespace PoeOverlay.Overlay;

/// <summary>
/// The overlay's visual tree, hosted as the <c>RootVisual</c> of a child <c>HwndSource</c>
/// (S3 4.0 D-SH20 / S4 12.6).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="UserControl"/> and not a <c>Window</c>. The layered parent is raw Win32
/// (<see cref="Interop.LayeredHostWindowFactory"/>) because a WPF <c>Window</c> cannot hold
/// <c>WS_EX_LAYERED</c> at all while <c>AllowsTransparency</c> is false, and turning that on costs
/// ClearType (<c>00-shell-measurements.md</c> §11.1, §8.1). In the hosted child ClearType survives
/// bit-identically to a plain opaque window (§11.3).
/// </para>
/// <para>
/// Nothing in this tree may be a <c>Popup</c>, <c>ToolTip</c> or <c>ContextMenu</c>. Those create
/// their own top-level HWNDs, outside the parent's layered path, so neither the colour key nor the
/// alpha applies to them (§11.4, §11.6).
/// </para>
/// </remarks>
public sealed partial class OverlayView : UserControl
{
    /// <summary>The body panel's colour, which is also the move-mode border's "off" colour.</summary>
    internal static readonly Brush BodyBrush = Freeze(Color.FromRgb(0x1E, 0x1E, 0x1E));

    /// <summary>The move-mode border's "on" colour (S3 4.6.2 D-SH10, the primary channel).</summary>
    internal static readonly Brush MoveModeBrush = Freeze(Color.FromRgb(0x7F, 0xB2, 0xFF));

    private readonly OverlayViewModel _viewModel;
    private ClippingRowsPanel? _rowsPanel;

    /// <summary>Builds the view and binds it.</summary>
    /// <param name="viewModel">The display state. Attached to the fan-out by the composition root.</param>
    /// <param name="icons">Resolves a row's slug to its picture (FR-04-6, S3 4.10).</param>
    /// <remarks>
    /// The icon converter goes into this view's resources after <c>InitializeComponent</c> — which
    /// is what fills <see cref="System.Windows.FrameworkElement.Resources"/> from the XAML — and
    /// before the <c>DataContext</c> that makes rows appear. A <c>{StaticResource}</c> inside a
    /// <c>DataTemplate</c> is resolved when the template is instantiated, not when the file is
    /// parsed, so by the time the first row asks for the key it is there.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <c>internal</c>, not <c>public</c>: <see cref="ItemIconSource"/> is internal like every other
    /// Shell type, and a public constructor cannot take one. The class itself stays public because
    /// the XAML-generated half is.
    /// </para>
    /// </remarks>
    internal OverlayView(OverlayViewModel viewModel, ItemIconSource icons)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(icons);

        _viewModel = viewModel;
        InitializeComponent();
        Resources[ItemIconConverter.ResourceKey] = new ItemIconConverter(icons);
        DataContext = viewModel;
    }

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

    /// <summary>Unsubscribes from the rows panel. Idempotent.</summary>
    internal void Detach()
    {
        if (_rowsPanel is null)
        {
            return;
        }

        _rowsPanel.HiddenCountChanged -= OnHiddenCountChanged;
        _rowsPanel = null;
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnHiddenCountChanged(object? sender, EventArgs e)
    {
        // Never inline: this fires from inside a measure pass, and the view model's reaction
        // rewrites a bound string.
        var count = _rowsPanel?.HiddenCount ?? 0;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _viewModel.HiddenRowCount = count);
    }
}
