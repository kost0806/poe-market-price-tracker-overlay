using System.Windows;
using System.Windows.Media;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace PoeOverlay.Overlay;

/// <summary>
/// Stacks rows vertically and reports how many did not fit (HLD D19 / S3 4.4.1).
/// </summary>
/// <remarks>
/// D19 forbids estimating the hidden count. This panel is the only honest source of it: the number
/// is whatever the arrange pass could not place, which is by construction the number a reader
/// cannot see. The marker's own height is reserved before any row is admitted, so the marker is
/// never itself the thing that gets clipped.
/// </remarks>
internal sealed class ClippingRowsPanel : Panel
{
    /// <summary>Registers with the overlay window as soon as the visual tree can answer for it.</summary>
    internal ClippingRowsPanel() => Loaded += OnLoaded;

    /// <summary>Backing store for <see cref="HiddenCount"/>.</summary>
    internal static readonly DependencyProperty HiddenCountProperty = DependencyProperty.Register(
        nameof(HiddenCount),
        typeof(int),
        typeof(ClippingRowsPanel),
        new FrameworkPropertyMetadata(0));

    /// <summary>Backing store for <see cref="ReservedHeight"/>.</summary>
    internal static readonly DependencyProperty ReservedHeightProperty = DependencyProperty.Register(
        nameof(ReservedHeight),
        typeof(double),
        typeof(ClippingRowsPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Raised when <see cref="HiddenCount"/> changes, from inside the measure pass.</summary>
    internal event EventHandler? HiddenCountChanged;

    /// <summary>How many children the last arrange pass could not place.</summary>
    internal int HiddenCount
    {
        get => (int)GetValue(HiddenCountProperty);
        private set => SetValue(HiddenCountProperty, value);
    }

    /// <summary>Height set aside for the "+n more" marker before rows are admitted.</summary>
    internal double ReservedHeight
    {
        get => (double)GetValue(ReservedHeightProperty);
        set => SetValue(ReservedHeightProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var budget = double.IsInfinity(availableSize.Height)
            ? double.PositiveInfinity
            : Math.Max(0d, availableSize.Height - ReservedHeight);

        var used = 0d;
        var width = 0d;
        var hidden = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var desired = child.DesiredSize;

            if (hidden > 0 || used + desired.Height > budget)
            {
                hidden++;
                continue;
            }

            used += desired.Height;
            width = Math.Max(width, desired.Width);
        }

        if (hidden != HiddenCount)
        {
            HiddenCount = hidden;
            HiddenCountChanged?.Invoke(this, EventArgs.Empty);
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            used);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var budget = Math.Max(0d, finalSize.Height);
        var offset = 0d;
        var clipped = false;

        foreach (UIElement child in InternalChildren)
        {
            var desired = child.DesiredSize;
            if (clipped || offset + desired.Height > budget)
            {
                clipped = true;
                child.Arrange(new Rect(0d, 0d, 0d, 0d));
                continue;
            }

            child.Arrange(new Rect(0d, offset, finalSize.Width, desired.Height));
            offset += desired.Height;
        }

        return finalSize;
    }

    /// <summary>
    /// Registers with the hosting view by walking the visual tree.
    /// </summary>
    /// <remarks>
    /// <c>Window.GetWindow</c> used to do this and now cannot: the overlay's content is the
    /// <c>RootVisual</c> of an <c>HwndSource</c> child inside a raw Win32 parent, so there is no
    /// <c>Window</c> anywhere above this panel and <c>GetWindow</c> returns null (S3 4.0 D-SH20).
    /// </remarks>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is OverlayView view)
            {
                view.AttachRowsPanel(this);
                return;
            }
        }
    }
}
