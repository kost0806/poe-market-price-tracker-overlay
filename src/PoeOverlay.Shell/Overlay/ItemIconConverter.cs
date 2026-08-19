using System.Globalization;
using System.Windows.Data;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Overlay;

/// <summary>
/// Binds a row's <see cref="ItemId"/> to its picture (S3 4.10 / S4 12.7).
/// </summary>
/// <remarks>
/// The row carries the slug and nothing else — the view picks the icon rather than being handed
/// one, which is why <c>PriceRowViewModel</c> did not have to change for FR-04-6.
/// <para>
/// One instance per <see cref="OverlayView"/>: <c>OverlayView.xaml</c> declares it in that view's
/// resources and the constructor hands it the source. Not a static — the cache's lifetime is the
/// view's.
/// </para>
/// <para>
/// It is declared in the XAML rather than inserted from code because a <c>{StaticResource}</c>
/// inside a compiled template resolves against the dictionaries that were in scope when the file
/// was parsed, never against the live element tree. The first shipped build inserted it after
/// <c>InitializeComponent</c> and crashed on its first layout pass
/// (<c>00-shell-measurements.md</c> §15). That is also why the source arrives through
/// <see cref="Attach"/>: a XAML-declared resource needs a parameterless constructor.
/// </para>
/// </remarks>
internal sealed class ItemIconConverter : IValueConverter
{
    /// <summary>The resource key. <c>OverlayView.xaml</c> names the same string.</summary>
    internal const string ResourceKey = "ItemIcon";

    private ItemIconSource? _source;

    /// <summary>Hands the converter the source it answers from. Called once, by the view.</summary>
    /// <param name="source">The manifest and bitmap cache.</param>
    /// <exception cref="InvalidOperationException">A second call — two caches behind one view.</exception>
    internal void Attach(ItemIconSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_source is not null)
        {
            // A programming error, not a run-time condition: the view attaches exactly once.
            throw new InvalidOperationException("The icon converter already has a source.");
        }

        _source = source;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="null"/> for anything that is not an <see cref="ItemId"/>, including
    /// the <c>DisconnectedItem</c> sentinel WPF passes while an item container is being recycled,
    /// and before <see cref="Attach"/> — the resource exists from the moment the XAML is parsed,
    /// which is earlier than the constructor can reach it.
    /// </remarks>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => _source is not null && value is ItemId id ? _source.Resolve(id) : null;

    /// <inheritdoc />
    /// <remarks>The overlay is display-only; there is no direction to convert back in.</remarks>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Item icons are one-way.");
}
