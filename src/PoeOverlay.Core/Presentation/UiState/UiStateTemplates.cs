namespace PoeOverlay.Core.Presentation.UiState;

/// <summary>
/// Compile-time fallbacks for the <c>ui.state.*</c>, <c>ui.tray.*</c> and <c>ui.overlay.*</c> keys
/// (S3 9.3 D-PS8 / S4 11.8, 18.3).
/// </summary>
/// <remarks>
/// The twin of <c>PriceTemplates</c>, and it exists for the same reason: a key that fails to resolve
/// falls through to the key string itself, and a key string in place of
/// <c>rate pending for {0}</c> does not merely read badly — the number disappears. S2 4.6.4 settled
/// that every placeholder-bearing <c>ui.*</c> key carries a constant, and S4 14's corrected rule
/// adds the five placeholder-free strings the user has to be able to read (polling stopped,
/// rejected, inherited, unresolved, dropped). Tray menu labels are not in that group.
/// <para>
/// Every value here must match S4 14 character for character; <c>UiStateTemplateFallbackTests</c>
/// walks the class by reflection and fails on any drift.
/// </para>
/// </remarks>
internal static class UiStateTemplates
{
    /// <summary><c>{0}</c> is an already-formatted duration such as <c>3m</c>.</summary>
    public const string RatePendingWithDuration = "rate pending for {0}";

    /// <summary><c>{0}</c> is an already-formatted relative time such as <c>3m ago</c>.</summary>
    public const string PollingStoppedStale = "updates are delayed. last attempt {0}";

    /// <summary>The <c>LoopExited</c> branch, which no elapsed time can clear (S3 2.2).</summary>
    public const string PollingStoppedExited = "updates have stopped. restart the app";

    /// <summary>HLD 6.4 requires an overlay banner for this condition (S3 5.5 C-1).</summary>
    public const string CommitRejectedBanner = "prices are not updating. check the league setting";

    /// <summary>The footer line for a carried-over rate (S2 10.5).</summary>
    public const string RateInheritedFooter = "rate carried over";

    /// <summary>The row text that stops the UI telling the user to delete a perfectly good item.</summary>
    public const string ItemDroppedRow = "price unavailable — item still exists";

    /// <summary>The row text for an item that really is not in the response.</summary>
    public const string ItemUnresolvedRow = "item not found";

    /// <summary><c>{0}</c> is the number of states the tooltip could not fit.</summary>
    public const string TrayTooltipMore = "(+{0} more)";

    /// <summary><c>{0}</c> is the number of rows the overlay could not draw.</summary>
    public const string MoreRows = "+{0} more";

    /// <summary>The same count, when the user's own explicit height is what clipped them (S3 4.4.2).</summary>
    public const string MoreRowsExplicit = "+{0} more — adjust height in settings";

    /// <summary><c>{0}</c> is an already-formatted relative time.</summary>
    public const string FetchFailedRow = "update failed {0}";

    /// <summary><c>{0}</c> is the number of categories that failed.</summary>
    public const string FetchFailedBadge = "{0} categories failed to update";

    /// <summary><c>{0}</c> is the log path that could not be opened (S3 5.5 M5).</summary>
    public const string LoggingUnavailableWithPath = "log file unavailable — path: {0}";
}

/// <summary>The key literals paired with <see cref="UiStateTemplates"/> (S4 14.3, 14.4, 14.6, 18.3).</summary>
/// <remarks>
/// The placeholder-free banner keys that carry no constant are listed here too: the fallback chain's
/// fifth level (the key itself) is a good enough answer for them, but the key still has to be spelt
/// in exactly one place.
/// </remarks>
internal static class UiStateKeys
{
    /// <summary>Paired with <see cref="UiStateTemplates.RatePendingWithDuration"/>.</summary>
    public const string RatePendingDuration = "ui.state.ratePendingDuration";

    /// <summary>Paired with <see cref="UiStateTemplates.PollingStoppedStale"/>.</summary>
    public const string PollingStoppedStale = "ui.state.pollingStoppedStale";

    /// <summary>Paired with <see cref="UiStateTemplates.PollingStoppedExited"/>.</summary>
    public const string PollingStoppedExited = "ui.state.pollingStoppedExited";

    /// <summary>Paired with <see cref="UiStateTemplates.CommitRejectedBanner"/>.</summary>
    public const string CommitRejected = "ui.state.commitRejected";

    /// <summary>Paired with <see cref="UiStateTemplates.RateInheritedFooter"/>.</summary>
    public const string RateInherited = "ui.state.rateInherited";

    /// <summary>Paired with <see cref="UiStateTemplates.ItemDroppedRow"/>.</summary>
    public const string ItemDropped = "ui.state.itemDropped";

    /// <summary>Paired with <see cref="UiStateTemplates.ItemUnresolvedRow"/>.</summary>
    public const string ItemUnresolved = "ui.state.itemUnresolved";

    /// <summary>Paired with <see cref="UiStateTemplates.TrayTooltipMore"/>.</summary>
    public const string TrayTooltipMore = "ui.tray.tooltipMore";

    /// <summary>Paired with <see cref="UiStateTemplates.MoreRows"/>.</summary>
    public const string MoreRows = "ui.overlay.moreRows";

    /// <summary>Paired with <see cref="UiStateTemplates.MoreRowsExplicit"/>.</summary>
    public const string MoreRowsExplicit = "ui.overlay.moreRowsExplicit";

    /// <summary>Paired with <see cref="UiStateTemplates.FetchFailedRow"/>.</summary>
    public const string FetchFailedRow = "ui.state.fetchFailedRow";

    /// <summary>Paired with <see cref="UiStateTemplates.FetchFailedBadge"/>.</summary>
    public const string FetchFailedBadge = "ui.state.fetchFailedBadge";

    /// <summary>Paired with <see cref="UiStateTemplates.LoggingUnavailableWithPath"/>.</summary>
    public const string LoggingUnavailable = "ui.state.loggingUnavailable";

    /// <summary>No constant: no placeholder, and the settings window is not the last resort for it.</summary>
    public const string LeagueUnresolved = "ui.state.leagueUnresolved";

    /// <summary>No constant (S4 14.4).</summary>
    public const string SettingsWriteFailed = "ui.state.settingsWriteFailed";

    /// <summary>No constant (S4 14.4).</summary>
    public const string SettingsCorrupt = "ui.state.settingsCorrupt";

    /// <summary>No constant (S4 14.4).</summary>
    public const string SettingsReadOnly = "ui.state.settingsReadOnly";

    /// <summary>No constant (S4 14.4).</summary>
    public const string SettingsUnreadable = "ui.state.settingsUnreadable";

    /// <summary>No constant (S4 14.4).</summary>
    public const string TrayUnavailable = "ui.state.trayUnavailable";

    /// <summary>No constant (S4 14.4).</summary>
    public const string ViewModelRefreshFailing = "ui.state.viewModelRefreshFailing";

    /// <summary>The tray application name (S4 14.6).</summary>
    public const string TrayAppName = "ui.tray.appName";

    /// <summary>The attribution line both surfaces share (NFR-05, S3 5.4).</summary>
    public const string FooterAttribution = "ui.footer.attribution";
}
