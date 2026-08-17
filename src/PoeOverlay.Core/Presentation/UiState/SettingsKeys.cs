namespace PoeOverlay.Core.Presentation.UiState;

/// <summary>
/// The <c>ui.settings.*</c> key literals — the settings window's fixed strings (S3 5.4.4 D-SH23 /
/// S4 14.9).
/// </summary>
/// <remarks>
/// <para>
/// These keys did not exist until the Korean dictionary was written, because the window's labels
/// were English literals in XAML and no dictionary could reach them (S3 0.6 E11). FR-07-1 had
/// listed §5.4 as its satisfaction all along; §5.4.4 is where that claim finally became true.
/// </para>
/// <para>
/// None of them carries a compile-time constant. S4 14's D1 rule reserves constants for keys with
/// placeholders and for the state text a user must be able to read; every key here is a static
/// label with no placeholder, so falling through to level ⑤ prints the key — a diagnostic, not a
/// loss of function. Tray menu labels are treated the same way.
/// </para>
/// </remarks>
internal static class SettingsKeys
{
    /// <summary>The window caption.</summary>
    public const string Title = "ui.settings.title";

    /// <summary>First-run guidance, line one (FR-08-6).</summary>
    public const string FirstRunOverflow = "ui.settings.firstRun.overflow";

    /// <summary>First-run guidance, line two (FR-08-6).</summary>
    public const string FirstRunTaskbar = "ui.settings.firstRun.taskbar";

    /// <summary>Dismisses the first-run guidance for good.</summary>
    public const string FirstRunDismiss = "ui.settings.firstRun.dismiss";

    /// <summary>Search group header.</summary>
    public const string SearchHeader = "ui.settings.search.header";

    /// <summary>Adds the selected search hit to the watchlist.</summary>
    public const string AddToWatchlist = "ui.settings.search.add";

    /// <summary>Watchlist group header.</summary>
    public const string WatchlistHeader = "ui.settings.watchlist.header";

    /// <summary>Removes one watchlist row.</summary>
    public const string WatchlistRemove = "ui.settings.watchlist.remove";

    /// <summary>The group holding league, period, language, currency and opacity.</summary>
    public const string PreferencesHeader = "ui.settings.preferences.header";

    /// <summary>Label of the league override box.</summary>
    public const string League = "ui.settings.league";

    /// <summary>Label of the read-only "which league is actually in use" row (S3 5.4.3).</summary>
    public const string LeagueInUse = "ui.settings.leagueInUse";

    /// <summary>Label of the league-list status row.</summary>
    public const string LeagueList = "ui.settings.leagueList";

    /// <summary>Re-fetches the league list.</summary>
    public const string LeagueReload = "ui.settings.leagueReload";

    /// <summary>Retries the failing categories, cooldown ignored (S3 5.5).</summary>
    public const string RetryNow = "ui.settings.retryNow";

    /// <summary>Label of the refresh-interval box.</summary>
    public const string RefreshMinutes = "ui.settings.refreshMinutes";

    /// <summary>Label of the language box.</summary>
    public const string Language = "ui.settings.language";

    /// <summary>Label of the default display-currency box (FR-04-3).</summary>
    public const string DisplayCurrency = "ui.settings.displayCurrency";

    /// <summary>Label of the opacity slider (FR-05-5).</summary>
    public const string Opacity = "ui.settings.opacity";

    /// <summary>Shown while settings writes are blocked (D17).</summary>
    public const string WritesBlocked = "ui.settings.writesBlocked";

    /// <summary>Overlay placement group header.</summary>
    public const string PlacementHeader = "ui.settings.placement.header";

    /// <summary>The one setting that is deliberately not persisted (FR-05-6, D18-b).</summary>
    public const string MoveMode = "ui.settings.placement.moveMode";

    /// <summary>Returns the overlay to content-driven height (S3 4.4).</summary>
    public const string RevertHeight = "ui.settings.placement.revertHeight";

    /// <summary>Returns the overlay to its default placement (D22).</summary>
    public const string ResetPlacement = "ui.settings.placement.reset";

    /// <summary>Diagnostics group header.</summary>
    public const string DiagnosticsHeader = "ui.settings.diagnostics.header";

    /// <summary>Opens the log folder (D12).</summary>
    public const string OpenLogFolder = "ui.settings.diagnostics.openLogFolder";

    /// <summary>Re-attempts tray registration (D-SH12).</summary>
    public const string RetryTrayRegistration = "ui.settings.diagnostics.retryTray";

    /// <summary>Clears a corrupt-settings write block (D-SE2).</summary>
    public const string AcknowledgeCorrupt = "ui.settings.diagnostics.acknowledgeCorrupt";

    /// <summary>Header that tells the list what a warning means (S3 5.4.3).</summary>
    public const string RecentErrorsHeader = "ui.settings.diagnostics.recentHeader";

    /// <summary>Reminds the user that closing this window is not exiting (FR-08-4).</summary>
    public const string CloseNotice = "ui.settings.closeNotice";

    /// <summary>Search found matches.</summary>
    public const string SearchFound = "ui.settings.search.found";

    /// <summary>The cache has data, and none of it matches.</summary>
    public const string SearchNotInCache = "ui.settings.search.notInCache";

    /// <summary>Nothing has been fetched yet — not the same claim (S2 6.7, S3 5.4.3).</summary>
    public const string SearchCacheEmpty = "ui.settings.search.cacheEmpty";

    /// <summary>A catalogue hit whose category has never been fetched (S3 5.4.6, D-DL29).</summary>
    public const string SearchNoPrice = "ui.settings.search.noPrice";

    /// <summary>The league list loaded normally.</summary>
    public const string LeagueStatusOk = "ui.settings.leagueStatus.ok";

    /// <summary>The list loaded but its shape is wrong (D6).</summary>
    public const string LeagueStatusSuspicious = "ui.settings.leagueStatus.suspicious";

    /// <summary>The list could not be loaded at all.</summary>
    public const string LeagueStatusFailed = "ui.settings.leagueStatus.failed";
}
