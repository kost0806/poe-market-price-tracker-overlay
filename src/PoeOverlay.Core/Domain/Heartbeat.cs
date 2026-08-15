namespace PoeOverlay.Core.Domain;

/// <summary>What started a round (S2 2.8).</summary>
public enum RoundTrigger
{
    Startup,
    Scheduled,
    Repoll,
    LeagueChanged,
}

/// <summary>How a round ended (S2 2.8).</summary>
public enum RoundOutcome
{
    Completed,
    PartiallyFailed,
    AllFailed,
    LeagueUnresolved,
    Canceled,
}

/// <summary>Why the polling loop left its outermost frame (S2 2.8).</summary>
public enum LoopExitKind
{
    Canceled,
    Faulted,
}

/// <summary>
/// Liveness of the polling loop (S2 2.8 / S4 3.6).
/// </summary>
/// <remarks>
/// Invariant: <c>LoopExited =&gt; ExitKind is not null &amp;&amp; ExitedAt is not null</c>.
/// <see cref="LastRoundAttemptAt"/> is written at the start of every round regardless of outcome (D20).
/// <para>
/// The instants are nullable because <c>default(DateTimeOffset)</c> is 0001-01-01 rather than
/// "absent": non-nullable fields would make the stall verdict true on the very first 30 s tick,
/// putting a "polling stopped" banner on screen next to a Loading row.
/// </para>
/// </remarks>
public sealed record Heartbeat(
    DateTimeOffset? LastRoundAttemptAt,
    int LastRoundNumber,
    DateTimeOffset? LastRoundCompletedAt,
    RoundOutcome? LastOutcome,
    bool LoopExited,
    LoopExitKind? ExitKind,
    DateTimeOffset? ExitedAt);
