namespace PoeOverlay.Core.Domain;

/// <summary>One league as offered by the league endpoint (S2 2.9 / S4 3.7).</summary>
public sealed record LeagueEntry(string Id, string Name);

/// <summary>Verdict on a fetched league list (S2 2.9).</summary>
public enum LeagueListStatus
{
    Ok,
    Suspicious,
    Failed,
}

/// <summary>
/// A fetched league list plus its verdict (S2 2.9 / S4 3.7).
/// </summary>
/// <remarks>
/// Invariants: <c>Ok</c> and <c>Suspicious</c> both imply at least one entry — a suspicious list
/// is still a usable list, so the dropdown is still populated — and <c>Failed</c> implies no
/// entries and a non-null <see cref="FailureCode"/>.
/// Market only renders the verdict; turning <c>Suspicious</c> into an unresolved league is
/// Polling's decision (D6).
/// </remarks>
public sealed record LeagueList(
    IReadOnlyList<LeagueEntry> Entries,
    DateTimeOffset FetchedAt,
    LeagueListStatus Status,
    string? FailureCode);
