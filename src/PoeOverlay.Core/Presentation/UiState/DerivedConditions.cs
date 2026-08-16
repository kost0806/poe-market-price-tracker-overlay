using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.ViewModels.Rows;
using PoeOverlay.Core.Pricing;

namespace PoeOverlay.Core.Presentation.UiState;

/// <summary>
/// The derived state S2 10.5 assigned to Presentation, as pure functions (S3 9.2 / S4 11.8, 18.4).
/// </summary>
/// <remarks>
/// None of these takes a <c>TimeProvider</c>. <paramref name="now"/> is a parameter everywhere, and
/// that is the enforcement of "one pass, one <c>now</c>" (D-PR7): rows computed against different
/// clock reads can disagree about whether the rate expired, and that defect reproduces only as a
/// screenshot. The thresholds come from <see cref="StalenessPolicy"/> rather than from local
/// constants, so changing the refresh interval cannot move one of them and not the other (D-C2).
/// </remarks>
internal static class DerivedConditions
{
    /// <summary>
    /// Which of the two <c>PollingStopped</c> branches holds, if either (S4 18.4 D-DL22).
    /// </summary>
    /// <remarks>
    /// A null <c>LastRoundAttemptAt</c> is not a stall. <c>default(DateTimeOffset)</c> is year 0001,
    /// so treating absence as an instant puts "polling stopped" on screen next to a Loading row on
    /// the first 30 s tick (measured, PL0).
    /// </remarks>
    public static (bool IsStopped, bool IsExited) PollingStoppedBranch(
        Heartbeat heartbeat,
        DateTimeOffset now,
        int refreshIntervalMinutes)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);

        if (heartbeat.LoopExited)
        {
            return (true, true);
        }

        if (heartbeat.LastRoundAttemptAt is not { } attemptedAt)
        {
            return (false, false);
        }

        return now - attemptedAt > StalenessPolicy.HeartbeatStaleAfter(refreshIntervalMinutes)
            ? (true, false)
            : (false, false);
    }

    /// <summary>The <c>IsStopped</c> half of <see cref="PollingStoppedBranch"/>.</summary>
    public static bool IsPollingStopped(Heartbeat heartbeat, DateTimeOffset now, int refreshIntervalMinutes)
        => PollingStoppedBranch(heartbeat, now, refreshIntervalMinutes).IsStopped;

    /// <summary>
    /// True when there is no usable divine rate (S2 10.5 D-PL4).
    /// </summary>
    /// <remarks>
    /// Derived rather than stored so that this verdict and Pricing's rate gate are the same
    /// judgement made against the same instant. Stored, the two drift by up to a full period and
    /// every row reads "rate pending" while the condition says otherwise.
    /// </remarks>
    public static bool IsRatePending(DivineRate? rate, DateTimeOffset now, int refreshIntervalMinutes)
        => rate is null || now - rate.AcquiredAt > StalenessPolicy.RateMaxAge(refreshIntervalMinutes);

    /// <summary>
    /// How long the rate has been pending, measured from <c>AcquiredAt + RateMaxAge</c> (S2 10.5).
    /// </summary>
    /// <remarks>
    /// Zero when there has never been a rate: the age of an absence is not a number this layer can
    /// honestly produce, and "rate pending for 0s" is the least misleading of the available lies.
    /// </remarks>
    public static TimeSpan RatePendingDuration(DivineRate? rate, DateTimeOffset now, int refreshIntervalMinutes)
    {
        if (rate is null)
        {
            return TimeSpan.Zero;
        }

        var expiredAt = rate.AcquiredAt + StalenessPolicy.RateMaxAge(refreshIntervalMinutes);
        var pending = now - expiredAt;
        return pending > TimeSpan.Zero ? pending : TimeSpan.Zero;
    }

    /// <summary>
    /// True when a row's data is old enough to mark (S2 10.5).
    /// </summary>
    /// <remarks>
    /// A raw <see cref="TimeSpan"/> comparison, never a comparison of the formatted string:
    /// <c>Relative</c> truncates, so "10m ago" covers 10:00 to 10:59 and a text-based verdict flips
    /// a minute early or late depending on which side of the truncation the sample lands.
    /// </remarks>
    public static bool IsRowStale(DateTimeOffset fetchedAt, DateTimeOffset now, int refreshIntervalMinutes)
        => now - fetchedAt > StalenessPolicy.RowStaleAfter(refreshIntervalMinutes);

    /// <summary>
    /// The four-way row verdict of S2 10.5 D-PL5.
    /// </summary>
    /// <param name="hasSnapshotEntry">True when a <c>CategorySnapshot</c> exists for the row's category.</param>
    /// <param name="consecutiveFailuresPositive">True when that category's status shows failures.</param>
    /// <param name="isInSkippedIds">True when the item appears in the snapshot's <c>SkippedIds</c>.</param>
    /// <remarks>
    /// <para>
    /// This function answers only "the item is not priced — why?". <see cref="RowKind.Normal"/> is
    /// decided by the caller, which is the only place that can look the id up in
    /// <c>CategorySnapshot.Items</c>; S4's three-boolean signature has no way to carry that fact.
    /// </para>
    /// <para>
    /// <c>SkippedIds</c> is the whole basis for the third branch. <c>primaryValue: 0</c> is an
    /// ordinary state (nothing listed), the line is skipped, and the item vanishes from a
    /// <em>successful</em> snapshot — a two-way split calls that "item not found" and the UI tells
    /// the user to delete something that exists.
    /// </para>
    /// </remarks>
    public static RowKind ClassifyRow(bool hasSnapshotEntry, bool consecutiveFailuresPositive, bool isInSkippedIds)
    {
        if (!hasSnapshotEntry)
        {
            return consecutiveFailuresPositive ? RowKind.FetchFailed : RowKind.Loading;
        }

        return isInSkippedIds ? RowKind.ItemDropped : RowKind.ItemUnresolved;
    }

    /// <summary>
    /// The overlay's overall state (S2 10.5, HLD 6.5).
    /// </summary>
    /// <remarks>
    /// <c>Loading</c> is not absorbing and it is not sticky either: it means "no round has finished
    /// yet". Once one has, the last outcome decides, and a later round in flight does not send the
    /// display back to Loading.
    /// </remarks>
    public static DisplayState ClassifyDisplayState(Heartbeat heartbeat)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);

        if (heartbeat.LastRoundCompletedAt is null)
        {
            return DisplayState.Loading;
        }

        return heartbeat.LastOutcome is RoundOutcome.AllFailed or RoundOutcome.LeagueUnresolved
            ? DisplayState.Failed
            : DisplayState.Ready;
    }

    /// <summary>
    /// The most recent successful fetch across all categories, or null when there is none.
    /// </summary>
    /// <remarks>
    /// "The last round in which every category succeeded" would be a functionally dead indicator —
    /// with eighteen categories it is almost never true, so the footer would stop updating.
    /// </remarks>
    public static DateTimeOffset? LatestFetchedAt(IReadOnlyDictionary<ExchangeCategory, CategorySnapshot> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        DateTimeOffset? latest = null;
        foreach (var snapshot in categories.Values)
        {
            if (latest is null || snapshot.FetchedAt > latest.Value)
            {
                latest = snapshot.FetchedAt;
            }
        }

        return latest;
    }

    /// <summary>How many categories currently show consecutive failures (S2 10.5).</summary>
    public static int FailedCategoryCount(IReadOnlyDictionary<ExchangeCategory, CategoryStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var count = 0;
        foreach (var status in statuses.Values)
        {
            if (status.ConsecutiveFailures > 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Which categories currently show consecutive failures — the set "retry now" acts on (S3 5.5).
    /// </summary>
    /// <remarks>
    /// Sorted, and empty rather than a fresh empty list when nothing is failing. Dictionary order is
    /// an implementation detail of whatever built the map, and a retry that visits eighteen
    /// categories in an order that changes between passes reports its failures in an order the user
    /// cannot make sense of. The cooldown is not consulted: a category in cooldown is exactly the
    /// one this command exists to reach.
    /// </remarks>
    public static IReadOnlyList<ExchangeCategory> FailingCategories(
        IReadOnlyDictionary<ExchangeCategory, CategoryStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        List<ExchangeCategory>? failing = null;
        foreach (var status in statuses.Values)
        {
            if (status.ConsecutiveFailures > 0)
            {
                (failing ??= []).Add(status.Category);
            }
        }

        if (failing is null)
        {
            return [];
        }

        failing.Sort();
        return failing;
    }

    /// <summary>True when <paramref name="kind"/> is present and active in the snapshot's map.</summary>
    public static bool IsActive(
        IReadOnlyDictionary<AppConditionKind, ConditionState> conditions,
        AppConditionKind kind)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        return conditions.TryGetValue(kind, out var state) && state.Active;
    }
}
