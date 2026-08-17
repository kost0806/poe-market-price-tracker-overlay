using System.Globalization;
using System.Windows.Data;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Overlay;

/// <summary>
/// Binds a row's <see cref="ItemId"/> to its picture (S3 4.10 / S4 12.7).
/// </summary>
/// <remarks>
/// The row carries the slug and nothing else — the view picks the icon, exactly as it picks the
/// change column's brush from <c>ChangeDirection</c> rather than being handed one (S3 4.8). That is
/// why <c>PriceRowViewModel</c> did not have to change for FR-04-6.
/// <para>
/// One instance per <see cref="OverlayView"/>, put into that view's resources by its constructor.
/// Not a static: the cache's lifetime is the view's.
/// </para>
/// </remarks>
internal sealed class ItemIconConverter : IValueConverter
{
    /// <summary>The resource key. <c>OverlayView.xaml</c> names the same string.</summary>
    internal const string ResourceKey = "ItemIcon";

    private readonly ItemIconSource _source;

    /// <summary>Wraps a source.</summary>
    /// <param name="source">The manifest and bitmap cache.</param>
    internal ItemIconConverter(ItemIconSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="null"/> for anything that is not an <see cref="ItemId"/>, including
    /// the <c>DisconnectedItem</c> sentinel WPF passes while an item container is being recycled.
    /// </remarks>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ItemId id ? _source.Resolve(id) : null;

    /// <inheritdoc />
    /// <remarks>The overlay is display-only; there is no direction to convert back in.</remarks>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Item icons are one-way.");
}
