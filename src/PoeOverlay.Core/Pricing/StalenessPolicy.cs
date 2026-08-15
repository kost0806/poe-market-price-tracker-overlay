namespace PoeOverlay.Core.Pricing;

/// <summary>
/// The three age thresholds derived from the refresh interval (S2 4.5.3 / S4 6.3).
/// </summary>
/// <remarks>
/// The one use D-C2 allows for the <c>Polling → Pricing</c> edge. If each module kept its own
/// constant, changing the interval would update one of them: were <c>Polling</c> to inherit a rate
/// that <c>Pricing</c> then judged expired, the store's rate would never reach the screen.
/// </remarks>
public static class StalenessPolicy
{
    private static readonly TimeSpan RateFloor = TimeSpan.FromMinutes(30);

    /// <summary>How long a divine rate stays usable: <c>max(30 min, 3 × interval)</c>.</summary>
    public static TimeSpan RateMaxAge(int refreshIntervalMinutes)
    {
        var scaled = TimeSpan.FromMinutes(3d * refreshIntervalMinutes);
        return scaled > RateFloor ? scaled : RateFloor;
    }

    /// <summary>When a row is marked stale: <c>2 × interval</c>.</summary>
    public static TimeSpan RowStaleAfter(int refreshIntervalMinutes)
        => TimeSpan.FromMinutes(2d * refreshIntervalMinutes);

    /// <summary>When the heartbeat is judged stale: <c>2 × interval + 1 min</c>.</summary>
    public static TimeSpan HeartbeatStaleAfter(int refreshIntervalMinutes)
        => TimeSpan.FromMinutes((2d * refreshIntervalMinutes) + 1d);
}
