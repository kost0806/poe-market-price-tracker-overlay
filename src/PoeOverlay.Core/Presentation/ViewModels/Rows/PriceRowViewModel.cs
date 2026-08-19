using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Pricing;

namespace PoeOverlay.Core.Presentation.ViewModels.Rows;

/// <summary>Why a row looks the way it does (S2 10.5 D-PL5 / S4 11.3).</summary>
public enum RowKind
{
    /// <summary>A priced row.</summary>
    Normal,

    /// <summary>No data for this category yet.</summary>
    Loading,

    /// <summary>No data, and the category has been failing.</summary>
    FetchFailed,

    /// <summary>Priced data arrived and the item was not in it.</summary>
    ItemUnresolved,

    /// <summary>Priced data arrived and the item's line was skipped — the item still exists.</summary>
    ItemDropped,
}

/// <summary>
/// One overlay row, fully formatted (S4 11.3).
/// </summary>
/// <remarks>
/// A record, so a pass that produces an equal row produces an equal value and the view has nothing
/// to redraw. The row carries no brush, no icon and no pixel: colour is the view's decision from
/// <see cref="Kind"/>, and geometry never reaches this layer at all (S2 10.7).
///
/// There is no change percentage here. The API's is a seven-day figure denominated in the row's
/// max-volume currency, so it says nothing about the price beside it (FR-04-1, contract 3.2).
/// </remarks>
/// <param name="RelativeTime">Already formatted ("3m ago"), because the verdict below is not.</param>
/// <param name="IsStale">A raw <see cref="TimeSpan"/> verdict, not a reading of <paramref name="RelativeTime"/>.</param>
public sealed record PriceRowViewModel(
    ItemId Id,
    string DisplayName,
    PriceDisplay Price,
    string RelativeTime,
    bool IsRateInherited,
    bool IsStale,
    RowKind Kind);
