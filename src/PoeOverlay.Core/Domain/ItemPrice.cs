namespace PoeOverlay.Core.Domain;

/// <summary>
/// One priced line as mapped from a poe.ninja overview response (S2 2.5 / S4 3.5).
/// </summary>
/// <param name="Id">Slug key.</param>
/// <param name="ApiName">The root items[].name — the name table, not core.items (contract §2.0); null when the join misses, which only shortens the name fallback chain.</param>
/// <param name="PrimaryValue">Value in core.primary (chaos) units. Market enforces &gt; 0 and &gt;= MinPrice.</param>
/// <param name="VolumePrimaryValue">Nullable so that a missing volume does not silently become 0 and does not discard the price.</param>
/// <param name="MaxVolumeCurrency">Raw token, never normalised. Pricing interprets it; Market records unknown tokens.</param>
/// <param name="MaxVolumeRate">Held for cross-checking only. Never an input to any calculation (FR-04-5, D1).</param>
/// <param name="TotalChangePercent">sparkline.totalChange.</param>
/// <param name="SelfReportedCategory">core.items[].category (contract A6).</param>
public sealed record ItemPrice(
    ItemId Id,
    string? ApiName,
    decimal PrimaryValue,
    double? VolumePrimaryValue,
    string? MaxVolumeCurrency,
    decimal? MaxVolumeRate,
    double? TotalChangePercent,
    ExchangeCategory? SelfReportedCategory);
