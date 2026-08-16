using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.ViewModels.Rows;
using PoeOverlay.Core.Pricing;

namespace PoeOverlay.Core.Presentation.UiState;

/// <summary>
/// Turns the condition map into banner lines (S3 5.5, 7.6).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of <c>snapshot</c>, <c>now</c> and the interval, shared by the overlay and the
/// settings window so the two cannot disagree about what is wrong. That purity is what lets both
/// view models run it as their <em>first</em> step: the banner list is exactly the thing the user
/// needs when the rest of a <c>Refresh</c> is unstable, so it must not sit downstream of the code
/// that can throw (S3 7.6 M5).
/// </para>
/// <para>
/// The order is S3 5.5's overlay priority — actionability × severity — extended past the sixth
/// slot with the conditions the overlay deliberately never shows. The overlay takes the first
/// entry with a slot; the settings window shows the whole list, because its banner area scrolls.
/// </para>
/// </remarks>
internal static class BannerFactory
{
    /// <summary>Every active banner, most urgent first.</summary>
    public static IReadOnlyList<BannerViewModel> Assemble(
        MarketSnapshot snapshot,
        DateTimeOffset now,
        int refreshIntervalMinutes,
        ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(localizer);

        var banners = new List<BannerViewModel>();
        var conditions = snapshot.Conditions;

        Stored(banners, conditions, now, AppConditionKind.SettingsCorrupt, UiStateKeys.SettingsCorrupt, BannerSeverity.Error, localizer);
        Stored(banners, conditions, now, AppConditionKind.TrayUnavailable, UiStateKeys.TrayUnavailable, BannerSeverity.Error, localizer);
        Stored(banners, conditions, now, AppConditionKind.LeagueUnresolved, UiStateKeys.LeagueUnresolved, BannerSeverity.Error, localizer);

        if (DerivedConditions.IsActive(conditions, AppConditionKind.CommitRejected))
        {
            banners.Add(new BannerViewModel(
                AppConditionKind.CommitRejected,
                UiStateFormat.Ui(localizer, UiStateKeys.CommitRejected, UiStateTemplates.CommitRejectedBanner),
                Since(conditions, AppConditionKind.CommitRejected, now),
                BannerSeverity.Error));
        }

        Stored(banners, conditions, now, AppConditionKind.SettingsWriteFailed, UiStateKeys.SettingsWriteFailed, BannerSeverity.Warning, localizer);
        AddPollingStopped(banners, snapshot, now, refreshIntervalMinutes, localizer);

        // From here down: no overlay slot (S3 5.5). The settings window shows them all.
        Stored(banners, conditions, now, AppConditionKind.SettingsUnreadable, UiStateKeys.SettingsUnreadable, BannerSeverity.Error, localizer);
        Stored(banners, conditions, now, AppConditionKind.SettingsReadOnly, UiStateKeys.SettingsReadOnly, BannerSeverity.Warning, localizer);
        AddLoggingUnavailable(banners, conditions, now, localizer);
        Stored(banners, conditions, now, AppConditionKind.ViewModelRefreshFailing, UiStateKeys.ViewModelRefreshFailing, BannerSeverity.Error, localizer);

        AddFetchFailed(banners, snapshot, localizer);
        AddRateBanners(banners, snapshot, now, refreshIntervalMinutes, localizer);

        return banners;
    }

    /// <summary>
    /// The overlay's single banner slot, or null when nothing eligible is active (S3 5.5).
    /// </summary>
    /// <remarks>
    /// <c>ViewModelRefreshFailing</c> and <c>LoggingUnavailable</c> are excluded by design: the
    /// first is reported through the two channels that still work, and the second is not something
    /// the user can act on from the overlay.
    /// </remarks>
    public static BannerViewModel? TopOverlayBanner(IReadOnlyList<BannerViewModel> banners)
    {
        ArgumentNullException.ThrowIfNull(banners);

        foreach (var banner in banners)
        {
            if (HasOverlaySlot(banner.Kind))
            {
                return banner;
            }
        }

        return null;
    }

    /// <summary>Whether HLD 6.4 gives <paramref name="kind"/> the overlay column.</summary>
    public static bool HasOverlaySlot(AppConditionKind kind)
        => kind is AppConditionKind.SettingsCorrupt
            or AppConditionKind.TrayUnavailable
            or AppConditionKind.LeagueUnresolved
            or AppConditionKind.CommitRejected
            or AppConditionKind.SettingsWriteFailed
            or AppConditionKind.PollingStopped;

    private static void Stored(
        List<BannerViewModel> banners,
        IReadOnlyDictionary<AppConditionKind, ConditionState> conditions,
        DateTimeOffset now,
        AppConditionKind kind,
        string key,
        BannerSeverity severity,
        ILocalizer localizer)
    {
        if (!DerivedConditions.IsActive(conditions, kind))
        {
            return;
        }

        banners.Add(new BannerViewModel(kind, localizer.Ui(key), Since(conditions, kind, now), severity));
    }

    private static void AddPollingStopped(
        List<BannerViewModel> banners,
        MarketSnapshot snapshot,
        DateTimeOffset now,
        int refreshIntervalMinutes,
        ILocalizer localizer)
    {
        var heartbeat = snapshot.Heartbeat;
        var (isStopped, isExited) = DerivedConditions.PollingStoppedBranch(heartbeat, now, refreshIntervalMinutes);
        if (!isStopped)
        {
            return;
        }

        // The two branches are different problems: one clears itself on the next heartbeat, the
        // other only on a restart (S3 2.2). One banner text for both would either promise a
        // recovery that cannot happen or tell a recovering app to restart.
        if (isExited)
        {
            banners.Add(new BannerViewModel(
                AppConditionKind.PollingStopped,
                UiStateFormat.Ui(localizer, UiStateKeys.PollingStoppedExited, UiStateTemplates.PollingStoppedExited),
                heartbeat.ExitedAt is { } exitedAt ? Elapsed(exitedAt, now) : TimeSpan.Zero,
                BannerSeverity.Error));
            return;
        }

        var attemptedAt = heartbeat.LastRoundAttemptAt ?? now;
        banners.Add(new BannerViewModel(
            AppConditionKind.PollingStopped,
            UiStateFormat.Ui(
                localizer,
                UiStateKeys.PollingStoppedStale,
                UiStateTemplates.PollingStoppedStale,
                PricingEngine.Relative(attemptedAt, now, localizer)),
            Elapsed(attemptedAt, now),
            BannerSeverity.Warning));
    }

    private static void AddLoggingUnavailable(
        List<BannerViewModel> banners,
        IReadOnlyDictionary<AppConditionKind, ConditionState> conditions,
        DateTimeOffset now,
        ILocalizer localizer)
    {
        if (!conditions.TryGetValue(AppConditionKind.LoggingUnavailable, out var state) || !state.Active)
        {
            return;
        }

        banners.Add(new BannerViewModel(
            AppConditionKind.LoggingUnavailable,
            UiStateFormat.Ui(
                localizer,
                UiStateKeys.LoggingUnavailable,
                UiStateTemplates.LoggingUnavailableWithPath,
                state.Detail ?? string.Empty),
            Elapsed(state.Since, now),
            BannerSeverity.Warning));
    }

    private static void AddFetchFailed(List<BannerViewModel> banners, MarketSnapshot snapshot, ILocalizer localizer)
    {
        // Derived, never stored: the Store rejects this member, and the failure list has always
        // come from CategoryStatuses (S2 2.11 5th ed. / S4 19.8).
        var failed = DerivedConditions.FailedCategoryCount(snapshot.CategoryStatuses);
        if (failed == 0)
        {
            return;
        }

        banners.Add(new BannerViewModel(
            AppConditionKind.FetchFailed,
            UiStateFormat.Ui(
                localizer,
                UiStateKeys.FetchFailedBadge,
                UiStateTemplates.FetchFailedBadge,
                UiStateFormat.Count(failed)),
            TimeSpan.Zero,
            BannerSeverity.Warning));
    }

    private static void AddRateBanners(
        List<BannerViewModel> banners,
        MarketSnapshot snapshot,
        DateTimeOffset now,
        int refreshIntervalMinutes,
        ILocalizer localizer)
    {
        if (DerivedConditions.IsRatePending(snapshot.Rate, now, refreshIntervalMinutes))
        {
            var pending = DerivedConditions.RatePendingDuration(snapshot.Rate, now, refreshIntervalMinutes);
            banners.Add(new BannerViewModel(
                AppConditionKind.RatePending,
                UiStateFormat.Ui(
                    localizer,
                    UiStateKeys.RatePendingDuration,
                    UiStateTemplates.RatePendingWithDuration,
                    UiStateFormat.Duration(pending)),
                pending,
                BannerSeverity.Info));
            return;
        }

        if (snapshot.Rate?.Inherited == true)
        {
            banners.Add(new BannerViewModel(
                AppConditionKind.RateInherited,
                UiStateFormat.Ui(localizer, UiStateKeys.RateInherited, UiStateTemplates.RateInheritedFooter),
                Elapsed(snapshot.Rate.AcquiredAt, now),
                BannerSeverity.Info));
        }
    }

    private static TimeSpan Since(
        IReadOnlyDictionary<AppConditionKind, ConditionState> conditions,
        AppConditionKind kind,
        DateTimeOffset now)
        => conditions.TryGetValue(kind, out var state) ? Elapsed(state.Since, now) : TimeSpan.Zero;

    /// <summary>Never negative: a clock that has run backwards must not produce a negative age.</summary>
    private static TimeSpan Elapsed(DateTimeOffset since, DateTimeOffset now)
    {
        var elapsed = now - since;
        return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
    }
}
