using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.UiState;

namespace PoeOverlay.Core.Presentation.ViewModels;

/// <summary>
/// Every fixed string the settings window draws, resolved once (S3 5.4.4 D-SH23 / S4 14.9).
/// </summary>
/// <remarks>
/// <para>
/// The window used to spell these in XAML, which is why no dictionary reached them (S3 0.6 E11).
/// They are gathered into one immutable bundle rather than thirty view model properties so that a
/// language change swaps the bundle and raises one notification instead of thirty.
/// </para>
/// <para>
/// The bundle lives in <c>Presentation</c> and not in <c>Shell</c> because the key constants are
/// <c>internal</c> to this assembly: <c>SettingsWindowFactory</c> spells
/// <c>ui.footer.attribution</c> as a bare literal for exactly that reason, and thirty more literals
/// across the assembly boundary is the shape of the defect this type exists to close.
/// </para>
/// <para>
/// The constructor is <c>internal</c>: the view binds to a bundle, it never builds one.
/// </para>
/// </remarks>
public sealed class SettingsStrings
{
    /// <summary>Resolves every key through <paramref name="localizer"/>'s fallback chain.</summary>
    internal SettingsStrings(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        Title = localizer.Ui(SettingsKeys.Title);
        FirstRunOverflow = localizer.Ui(SettingsKeys.FirstRunOverflow);
        FirstRunTaskbar = localizer.Ui(SettingsKeys.FirstRunTaskbar);
        FirstRunDismiss = localizer.Ui(SettingsKeys.FirstRunDismiss);
        SearchHeader = localizer.Ui(SettingsKeys.SearchHeader);
        AddToWatchlist = localizer.Ui(SettingsKeys.AddToWatchlist);
        WatchlistHeader = localizer.Ui(SettingsKeys.WatchlistHeader);
        WatchlistRemove = localizer.Ui(SettingsKeys.WatchlistRemove);
        PreferencesHeader = localizer.Ui(SettingsKeys.PreferencesHeader);
        League = localizer.Ui(SettingsKeys.League);
        LeagueInUse = localizer.Ui(SettingsKeys.LeagueInUse);
        LeagueList = localizer.Ui(SettingsKeys.LeagueList);
        LeagueReload = localizer.Ui(SettingsKeys.LeagueReload);
        RetryNow = localizer.Ui(SettingsKeys.RetryNow);
        RefreshMinutes = localizer.Ui(SettingsKeys.RefreshMinutes);
        Language = localizer.Ui(SettingsKeys.Language);
        DisplayCurrency = localizer.Ui(SettingsKeys.DisplayCurrency);
        Opacity = localizer.Ui(SettingsKeys.Opacity);
        WritesBlocked = localizer.Ui(SettingsKeys.WritesBlocked);
        PlacementHeader = localizer.Ui(SettingsKeys.PlacementHeader);
        MoveMode = localizer.Ui(SettingsKeys.MoveMode);
        RevertHeight = localizer.Ui(SettingsKeys.RevertHeight);
        ResetPlacement = localizer.Ui(SettingsKeys.ResetPlacement);
        DiagnosticsHeader = localizer.Ui(SettingsKeys.DiagnosticsHeader);
        OpenLogFolder = localizer.Ui(SettingsKeys.OpenLogFolder);
        RetryTrayRegistration = localizer.Ui(SettingsKeys.RetryTrayRegistration);
        AcknowledgeCorrupt = localizer.Ui(SettingsKeys.AcknowledgeCorrupt);
        RecentErrorsHeader = localizer.Ui(SettingsKeys.RecentErrorsHeader);
        CloseNotice = localizer.Ui(SettingsKeys.CloseNotice);

        // The overlay footer's key, reused rather than duplicated: S3 5.4 chose one attribution
        // wording for both surfaces. It sat in the window's constructor until now, which meant it
        // was the one string a language change could not reach.
        Attribution = localizer.Ui(UiStateKeys.FooterAttribution);
    }

    /// <summary>The window caption.</summary>
    public string Title { get; }

    /// <summary>First-run guidance, line one (FR-08-6).</summary>
    public string FirstRunOverflow { get; }

    /// <summary>First-run guidance, line two (FR-08-6).</summary>
    public string FirstRunTaskbar { get; }

    /// <summary>Dismisses the first-run guidance.</summary>
    public string FirstRunDismiss { get; }

    /// <summary>Search group header.</summary>
    public string SearchHeader { get; }

    /// <summary>Adds the selected hit to the watchlist.</summary>
    public string AddToWatchlist { get; }

    /// <summary>Watchlist group header.</summary>
    public string WatchlistHeader { get; }

    /// <summary>Removes one watchlist row.</summary>
    public string WatchlistRemove { get; }

    /// <summary>The league/period/language/currency/opacity group header.</summary>
    public string PreferencesHeader { get; }

    /// <summary>Label of the league override box.</summary>
    public string League { get; }

    /// <summary>Label of the read-only active-league row (S3 5.4.3).</summary>
    public string LeagueInUse { get; }

    /// <summary>Label of the league-list status row.</summary>
    public string LeagueList { get; }

    /// <summary>Re-fetches the league list.</summary>
    public string LeagueReload { get; }

    /// <summary>Retries the failing categories now (S3 5.5).</summary>
    public string RetryNow { get; }

    /// <summary>Label of the refresh-interval box.</summary>
    public string RefreshMinutes { get; }

    /// <summary>Label of the language box.</summary>
    public string Language { get; }

    /// <summary>Label of the default display-currency box (FR-04-3).</summary>
    public string DisplayCurrency { get; }

    /// <summary>Label of the opacity slider (FR-05-5).</summary>
    public string Opacity { get; }

    /// <summary>Shown while settings writes are blocked (D17).</summary>
    public string WritesBlocked { get; }

    /// <summary>Overlay placement group header.</summary>
    public string PlacementHeader { get; }

    /// <summary>The one setting that is not persisted (FR-05-6).</summary>
    public string MoveMode { get; }

    /// <summary>Returns the overlay to content-driven height.</summary>
    public string RevertHeight { get; }

    /// <summary>Returns the overlay to its default placement.</summary>
    public string ResetPlacement { get; }

    /// <summary>Diagnostics group header.</summary>
    public string DiagnosticsHeader { get; }

    /// <summary>Opens the log folder.</summary>
    public string OpenLogFolder { get; }

    /// <summary>Re-attempts tray registration (D-SH12).</summary>
    public string RetryTrayRegistration { get; }

    /// <summary>Clears a corrupt-settings write block (D-SE2).</summary>
    public string AcknowledgeCorrupt { get; }

    /// <summary>Header explaining what a warning in the list means.</summary>
    public string RecentErrorsHeader { get; }

    /// <summary>Says that closing the window is not exiting (FR-08-4).</summary>
    public string CloseNotice { get; }

    /// <summary>The poe.ninja attribution line, shared with the overlay footer (NFR-05).</summary>
    public string Attribution { get; }
}
