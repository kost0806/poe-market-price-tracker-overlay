namespace PoeOverlay.Core.Domain;

/// <summary>
/// The category-listing slot used by cross search (S2 2.10 D-D5 / S4 3.7).
/// </summary>
/// <remarks>
/// Deliberately a separate slot from the round commit map: what lands here has not passed the
/// context checks (D8-c / D8-e). Merging the two would let differently validated data reach the
/// overlay display path with no type-level way to tell them apart. Only cross search reads both.
/// Editing the watchlist does not invalidate this slot — league is the only invalidation axis (D9).
/// </remarks>
public sealed record FetchedListing(
    IReadOnlyDictionary<ExchangeCategory, CategorySnapshot> ByCategory,
    string League,
    int DataEpoch);
