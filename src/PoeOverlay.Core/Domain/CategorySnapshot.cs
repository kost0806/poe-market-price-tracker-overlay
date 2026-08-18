namespace PoeOverlay.Core.Domain;

/// <summary>
/// Successful data for one category (S2 2.6 D-D4 / S4 3.5).
/// </summary>
/// <remarks>
/// Split from <see cref="CategoryStatus"/> so that FR-03-3 ("a failure does not erase a value")
/// holds structurally: the failure path can only touch the status, never this record.
/// <para>
/// Producer-enforced invariants: <c>Items</c> is a frozen dictionary of at least one entry whose
/// keys are all non-empty, <c>MedianPrimaryValue &gt; 0</c> computed once at mapping time,
/// <c>FetchedAt</c> is the mapping-completion instant rather than the request instant, and
/// <c>League</c>/<c>DataEpoch</c> tag the data world, never the round.
/// </para>
/// </remarks>
/// <param name="Items">Built as a FrozenDictionary; declared as the read-only interface.</param>
/// <param name="SkippedIds">Skipped slugs, capped at 200, so the UI can tell "unreadable" from "does not exist".</param>
/// <param name="ValidationBypassed">True when D8-e forced acceptance (S2 7.5).</param>
/// <param name="ETag">
/// The validator of this copy, as poe.ninja sent it (D24). It lives here rather than in a cache
/// inside Market so that there is only one thing to invalidate: a snapshot that goes away — a league
/// change, a new epoch — takes its validator with it. A dictionary could outlive its data and answer
/// a later <c>304</c> with nothing to show. Null when the response carried no ETag, which simply
/// makes the next request unconditional.
/// </param>
public sealed record CategorySnapshot(
    ExchangeCategory Category,
    IReadOnlyDictionary<ItemId, ItemPrice> Items,
    decimal MedianPrimaryValue,
    DateTimeOffset FetchedAt,
    string League,
    int DataEpoch,
    int RawLineCount,
    SkipCounts Skips,
    IReadOnlyList<ItemId> SkippedIds,
    bool SkippedIdsTruncated,
    int JoinMissCount,
    bool ValidationBypassed,
    string? ETag = null);
