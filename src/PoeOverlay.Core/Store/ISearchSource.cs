using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Store;

/// <summary>Which slot a hit came from (S2 6.7 / S4 8.1).</summary>
public enum SearchSource
{
    /// <summary>A round commit, which has passed the context checks.</summary>
    RoundCommitted,

    /// <summary>A user-initiated listing fetch, which has not.</summary>
    UserFetched,
}

/// <summary>
/// Why a search returned what it did (S2 6.7).
/// </summary>
/// <remarks>
/// Three outcomes, never one empty list. Telling the user "not in the cache" when the cache is
/// merely still empty makes them fetch categories by hand for something waiting would have
/// delivered — unnecessary traffic (NFR-02), and the first round arrives mid-fetch so the same
/// data lands twice.
/// </remarks>
public enum SearchOutcome
{
    /// <summary>At least one hit.</summary>
    Found,

    /// <summary>The cache holds data, none of it matching.</summary>
    NotInCache,

    /// <summary>Nothing has been fetched yet.</summary>
    CacheEmpty,
}

/// <summary>One search hit (S4 8.1).</summary>
public sealed record SearchHit(
    ItemId Id,
    string? ApiName,
    ExchangeCategory Category,
    SearchSource Source,
    decimal PrimaryValue,
    DateTimeOffset FetchedAt);

/// <summary>The result of one search (S4 8.1).</summary>
/// <param name="UnfetchedCategories">Categories with no data in either slot, so the UI can offer to fetch them.</param>
/// <param name="Truncated">True when more hits existed than the limit allowed.</param>
public sealed record SearchResult(
    IReadOnlyList<SearchHit> Hits,
    SearchOutcome Outcome,
    IReadOnlyList<ExchangeCategory> UnfetchedCategories,
    bool Truncated);

/// <summary>
/// Search tuning (S4 8.1).
/// </summary>
/// <param name="Limit">Clamped to S4 15.8's ceiling of 200.</param>
/// <param name="ExtraMatch">
/// Localised-name matching, injected because the Store may not reference Localization (S2 1.2).
/// The contract is D-ST6: it must be pure, it must not do I/O since it runs inside the Store's
/// iteration, and if it throws only that item counts as a mismatch — the search survives.
/// </param>
public sealed record SearchOptions(int Limit, Func<ItemId, string?, bool>? ExtraMatch);

/// <summary>Cross-category search over both cache slots (FR-01-1 / D7, S2 6.7 / S4 8.1).</summary>
public interface ISearchSource
{
    /// <summary>Searches both slots. Never throws for ordinary input.</summary>
    SearchResult Search(string query, SearchOptions options);
}
