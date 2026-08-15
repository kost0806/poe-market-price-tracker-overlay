namespace PoeOverlay.Core.Domain;

/// <summary>
/// The single immutable value the whole UI reads (S2 2.11 / S4 3.8).
/// </summary>
/// <remarks>
/// Top-level invariants:
/// <list type="bullet">
/// <item>INV-1 — a non-null <see cref="DataLeague"/> implies every category snapshot, the rate and the listing all carry that league.</item>
/// <item>INV-2 / INV-3 — every category snapshot and the listing carry <see cref="DataEpoch"/>.</item>
/// <item>INV-4 — a null <see cref="DataLeague"/> implies no categories, no rate and no listing.</item>
/// <item>INV-5 — a retreat of <see cref="LeagueResolution"/> from Resolved to Unresolved keeps categories, rate and listing. The only command that empties data is BeginNewLeague.</item>
/// <item>INV-6 — <see cref="Version"/> rises monotonically by exactly one per publish.</item>
/// <item>INV-7 — <see cref="DataEpoch"/> rises monotonically and only on a commit that changes <see cref="DataLeague"/>; watchlist and interval edits never touch it.</item>
/// <item>INV-8 — BeginNewLeague changes <see cref="DataLeague"/>, <see cref="DataEpoch"/> and <see cref="LeagueResolution"/> inside one command; no state exists where only two of the three moved.</item>
/// </list>
/// <see cref="RoundContext.RoundGeneration"/> is deliberately absent: putting the cancellation
/// axis into the snapshot would make the Store classify cancellation as contamination.
/// </remarks>
/// <param name="DataLeague">The world the data belongs to and the yardstick for commit validation (D-D6).</param>
/// <param name="RejectedCommitCount">Makes D9's "never silently dropped" observable.</param>
/// <param name="ConsecutiveEmptyCommitRounds">The sole basis for the CommitRejected condition.</param>
public sealed record MarketSnapshot(
    IReadOnlyDictionary<ExchangeCategory, CategorySnapshot> Categories,
    DivineRate? Rate,
    LeagueList? Leagues,
    FetchedListing? Listing,
    Heartbeat Heartbeat,
    ErrorRecord? LastError,
    IReadOnlyDictionary<ExchangeCategory, CategoryStatus> CategoryStatuses,
    LeagueResolution LeagueResolution,
    IReadOnlyDictionary<AppConditionKind, ConditionState> Conditions,
    string? DataLeague,
    int DataEpoch,
    long Version,
    int RejectedCommitCount,
    int ConsecutiveEmptyCommitRounds);
