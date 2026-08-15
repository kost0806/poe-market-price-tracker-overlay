namespace PoeOverlay.Core.Domain;

/// <summary>Whether the league for the current round is settled (S2 2.9).</summary>
public enum LeagueResolutionState
{
    Pending,
    Resolved,
    Unresolved,
}

/// <summary>
/// The league this round settled on, which is a different thing from the league list
/// (S2 2.9 / S4 3.7).
/// </summary>
/// <remarks>
/// Invariants: <c>Resolved &lt;=&gt; League is not null</c> and
/// <c>Unresolved =&gt; ReasonCode is not null</c>.
/// </remarks>
public sealed record LeagueResolution(LeagueResolutionState State, string? League, string? ReasonCode);
