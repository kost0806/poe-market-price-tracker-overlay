namespace PoeOverlay.Core.Polling;

/// <summary>
/// The fixed numbers the round loop runs on (S2 7 / S4 15.2).
/// </summary>
/// <remarks>
/// Everything derived from <c>refreshIntervalMinutes</c> lives in <c>Pricing.StalenessPolicy</c>
/// instead, which is the single reason the <c>Polling → Pricing</c> edge exists (D-C2): duplicating
/// those thresholds here would let an interval change update one copy and not the other, and a rate
/// Polling inherited but Pricing judged expired would never reach the screen.
/// </remarks>
public static class PollingOptions
{
    /// <summary>S2 7.7 / S4 15.2 — edits are collected for this long before a repoll is considered.</summary>
    public static readonly TimeSpan RepollDebounceWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// S2 7.7 / S4 15.2 — the floor between the end of one round and the start of a repoll.
    /// </summary>
    /// <remarks>A request that arrives too early is delayed to this instant, never dropped.</remarks>
    public static readonly TimeSpan MinimumRepollSpacing = TimeSpan.FromSeconds(60);

    /// <summary>S2 7.7 — the cooldown multiplier never exceeds this, and there is no permanent exclusion.</summary>
    public const int MaxCooldownMultiplier = 8;

    /// <summary>S2 7.5 D8-e — a median that moves by more than this factor is rejected.</summary>
    public const decimal MedianJumpRatio = 5m;

    /// <summary>
    /// S2 7.5 / S4 15.2 — after this many consecutive rejections the next jump is accepted.
    /// </summary>
    /// <remarks>
    /// Without forced acceptance a genuine market spike would lock the category out of every future
    /// update, because each new value would be compared against the same stale baseline forever.
    /// </remarks>
    public const int MedianJumpsBeforeForcedAccept = 2;

    /// <summary>
    /// How long <c>QueuedCount &gt; 0</c> with <c>ActiveCount == 0</c> must hold before it is reported.
    /// </summary>
    /// <remarks>
    /// The only healthy way to observe that pair is the gateway's 250 ms minimum issue interval, so
    /// thirty seconds is roughly a hundred and twenty times the longest legitimate window, and it is
    /// well inside the ninety-second logical request timeout — the report therefore arrives while
    /// the requests are still hanging rather than after they have all failed and become
    /// indistinguishable from an outage.
    /// </remarks>
    public static readonly TimeSpan GatewayStallThreshold = TimeSpan.FromSeconds(30);

    /// <summary>How often the gateway counters are sampled while a round's fetches are in flight.</summary>
    public static readonly TimeSpan GatewaySampleInterval = TimeSpan.FromSeconds(1);
}
