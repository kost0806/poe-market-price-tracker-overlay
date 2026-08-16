using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;

namespace PoeOverlay.Core.Presentation.UiState;

/// <summary>
/// The <c>ui.category.*</c> keys and the lookup that turns an <see cref="ExchangeCategory"/> into
/// something a person reads (S3 5.4.3 E13 / S4 14.10).
/// </summary>
/// <remarks>
/// <para>
/// The enum member is poe.ninja's query type, not a name — <c>AllflameEmber</c> and
/// <c>DivinationCard</c> reached the screen exactly as spelt here, and no dictionary could
/// translate them because nothing ever looked them up.
/// </para>
/// <para>
/// GGG's own static groups are not a source for these eighteen: they do not map one-to-one onto
/// poe.ninja's query types — the <c>Fragments</c> group label covers ninja's <c>Fragment</c> and
/// <c>Scarab</c> together (<c>00-api-contract.md</c> §6). Eighteen lines are written by hand.
/// </para>
/// </remarks>
internal static class CategoryLabels
{
    /// <summary>
    /// The key for <paramref name="category"/>, or <see langword="null"/> when the enum has grown a
    /// member this table does not know.
    /// </summary>
    public static string? KeyFor(ExchangeCategory category)
        => category switch
        {
            ExchangeCategory.Currency => "ui.category.currency",
            ExchangeCategory.Fragment => "ui.category.fragment",
            ExchangeCategory.Runegraft => "ui.category.runegraft",
            ExchangeCategory.AllflameEmber => "ui.category.allflameEmber",
            ExchangeCategory.Tattoo => "ui.category.tattoo",
            ExchangeCategory.Omen => "ui.category.omen",
            ExchangeCategory.DjinnCoin => "ui.category.djinnCoin",
            ExchangeCategory.Ducat => "ui.category.ducat",
            ExchangeCategory.EnshroudingCrystal => "ui.category.enshroudingCrystal",
            ExchangeCategory.DivinationCard => "ui.category.divinationCard",
            ExchangeCategory.Artifact => "ui.category.artifact",
            ExchangeCategory.Oil => "ui.category.oil",
            ExchangeCategory.DeliriumOrb => "ui.category.deliriumOrb",
            ExchangeCategory.Scarab => "ui.category.scarab",
            ExchangeCategory.Astrolabe => "ui.category.astrolabe",
            ExchangeCategory.Fossil => "ui.category.fossil",
            ExchangeCategory.Resonator => "ui.category.resonator",
            ExchangeCategory.Essence => "ui.category.essence",
            _ => null,
        };

    /// <summary>
    /// The label to draw for <paramref name="category"/>.
    /// </summary>
    /// <remarks>
    /// An uncatalogued member falls back to the enum name rather than to the key string: a new
    /// member is a gap in this table, and <c>Heist</c> reads better than
    /// <c>ui.category.heist</c> while someone fills it in. S4 16.7's exhaustiveness test is what
    /// stops that state from lasting.
    /// </remarks>
    public static string Label(ILocalizer localizer, ExchangeCategory category)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        var key = KeyFor(category);
        return key is null ? category.ToString() : localizer.Ui(key);
    }
}
