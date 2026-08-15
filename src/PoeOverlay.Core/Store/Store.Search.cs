using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Store;

/// <summary>
/// Cross-category search over both cache slots (S2 6.7 / FR-01-1 / D7).
/// </summary>
public sealed partial class Store
{
    /// <summary>S4 15.8 — the hard ceiling on returned hits.</summary>
    internal const int SearchLimitCeiling = 200;

    private static readonly ExchangeCategory[] AllCategories = Enum.GetValues<ExchangeCategory>();

    /// <inheritdoc />
    public SearchResult Search(string query, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        var snapshot = Current;
        var unfetched = UnfetchedCategories(snapshot);

        // Three outcomes, never one empty list: "nothing fetched yet" and "fetched, no match" call
        // for different user actions, so collapsing them is a behavioural bug, not a wording one.
        var cacheEmpty = snapshot.Categories.Count == 0 && snapshot.Listing is null;
        var emptyOutcome = cacheEmpty ? SearchOutcome.CacheEmpty : SearchOutcome.NotInCache;

        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return new SearchResult([], emptyOutcome, unfetched, false);
        }

        var ranked = new List<RankedHit>();
        var seen = new HashSet<ItemId>();

        // Round commits first, so the same id found in both slots is reported once, from the slot
        // that passed the context checks. Source keeps the provenance in the type.
        foreach (var category in AllCategories)
        {
            if (snapshot.Categories.TryGetValue(category, out var committed))
            {
                Collect(committed, SearchSource.RoundCommitted, trimmed, options, ranked, seen);
            }
        }

        if (snapshot.Listing is { } listing)
        {
            foreach (var category in AllCategories)
            {
                if (listing.ByCategory.TryGetValue(category, out var fetched))
                {
                    Collect(fetched, SearchSource.UserFetched, trimmed, options, ranked, seen);
                }
            }
        }

        ranked.Sort(static (left, right) =>
        {
            var byRank = left.Rank.CompareTo(right.Rank);
            if (byRank != 0)
            {
                return byRank;
            }

            var byCategory = ((int)left.Hit.Category).CompareTo((int)right.Hit.Category);
            return byCategory != 0
                ? byCategory
                : string.CompareOrdinal(left.Hit.Id.Value, right.Hit.Id.Value);
        });

        var limit = Math.Clamp(options.Limit, 0, SearchLimitCeiling);
        var truncated = ranked.Count > limit;

        var hits = new List<SearchHit>(Math.Min(limit, ranked.Count));
        for (var i = 0; i < ranked.Count && i < limit; i++)
        {
            hits.Add(ranked[i].Hit);
        }

        return new SearchResult(hits, hits.Count > 0 ? SearchOutcome.Found : emptyOutcome, unfetched, truncated);
    }

    private static IReadOnlyList<ExchangeCategory> UnfetchedCategories(MarketSnapshot snapshot)
    {
        var unfetched = new List<ExchangeCategory>();
        foreach (var category in AllCategories)
        {
            if (snapshot.Categories.ContainsKey(category))
            {
                continue;
            }

            if (snapshot.Listing is { } listing && listing.ByCategory.ContainsKey(category))
            {
                continue;
            }

            unfetched.Add(category);
        }

        return unfetched;
    }

    /// <summary>
    /// Exact match, then prefix, then substring; rank 3 is an <c>ExtraMatch</c>-only hit.
    /// </summary>
    /// <remarks>
    /// <c>OrdinalIgnoreCase</c>, which does not contradict S2 2.1's ordinal, case-sensitive
    /// <em>identity</em>: identity and search are different operations, and a case-sensitive search
    /// makes "Vivid" find nothing while breaking no stated rule.
    /// </remarks>
    private static int MatchRank(string candidate, string query)
    {
        if (string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return candidate.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : int.MaxValue;
    }

    private void Collect(
        CategorySnapshot snapshot,
        SearchSource source,
        string query,
        SearchOptions options,
        List<RankedHit> ranked,
        HashSet<ItemId> seen)
    {
        foreach (var price in snapshot.Items.Values)
        {
            var rank = MatchRank(price.Id.Value, query);
            if (price.ApiName is { } name)
            {
                rank = Math.Min(rank, MatchRank(name, query));
            }

            if (rank == int.MaxValue && ExtraMatches(options, price))
            {
                rank = 3;
            }

            if (rank == int.MaxValue || !seen.Add(price.Id))
            {
                continue;
            }

            ranked.Add(new RankedHit(
                rank,
                new SearchHit(price.Id, price.ApiName, snapshot.Category, source, price.PrimaryValue, snapshot.FetchedAt)));
        }
    }

    private bool ExtraMatches(SearchOptions options, ItemPrice price)
    {
        if (options.ExtraMatch is not { } extra)
        {
            return false;
        }

#pragma warning disable CA1031 // S2 9.5 row 7: that one item counts as a mismatch, plus one Warning per session.
        try
        {
            return extra(price.Id, price.ApiName);
        }
        catch (Exception ex)
        {
            // A caller-supplied predicate must not be able to kill the whole search.
            if (_suppression.ShouldReport("store.extraMatchFault", ex.GetType().FullName ?? "unknown"))
            {
                Log(LogLevel.Warning, "ExtraMatchFault", "A search ExtraMatch predicate threw; that item is treated as a mismatch.", ex);
            }

            return false;
        }
#pragma warning restore CA1031
    }

    private readonly record struct RankedHit(int Rank, SearchHit Hit);
}
