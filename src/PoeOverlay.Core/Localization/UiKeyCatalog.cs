using System.Collections.Frozen;

namespace PoeOverlay.Core.Localization;

/// <summary>
/// The "central table" S2 3.7 refers to: every <c>ui.*</c> key and the number of arguments its call
/// site passes. Transcribed from the single authoritative catalogue, S4 14 (14.1–14.7) plus the two
/// overlay keys S4 18.3 adds to 14.3.
/// </summary>
/// <remarks>
/// <para>
/// S2 3.7 requires load-time placeholder validation and says the expected argument count "comes
/// from the central table", but no upstream section gives that table a home in code — S4 2.1 lists
/// no file for it. This type is that home; without it the load-time check is unimplementable and a
/// translator's <c>"{0}c ({2}d)"</c> falls back silently and forever.
/// </para>
/// <para>
/// A <c>ui.*</c> key that is not listed here carries no expectation and is left alone, so adding a
/// key to a dictionary never costs a language its load.
/// </para>
/// </remarks>
internal static class UiKeyCatalog
{
    /// <summary>The <c>ui.</c> prefix that separates the two key spaces (S2 3.1).</summary>
    public const string UiPrefix = "ui.";

    /// <summary>Key of a dictionary's own display name (S2 3.2).</summary>
    public const string SelfNameKey = "ui.language.selfName";

    private static readonly FrozenDictionary<string, int> ArgumentCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // 14.1 ui.price.*
            ["ui.price.chaos"] = 1,
            ["ui.price.divine"] = 1,
            ["ui.price.chaosWithDivine"] = 2,
            ["ui.price.chaosRatePending"] = 1,
            ["ui.price.perChaos"] = 1,
            ["ui.price.perDivine"] = 1,
            ["ui.price.ratePending"] = 0,
            ["ui.price.unavailable"] = 0,
            ["ui.price.change"] = 2,

            // 14.2 ui.time.*
            ["ui.time.justNow"] = 0,
            ["ui.time.secondsAgo"] = 1,
            ["ui.time.minutesAgo"] = 1,
            ["ui.time.hoursAgo"] = 1,
            ["ui.time.daysAgo"] = 1,

            // 14.3 ui.state.* with placeholders
            ["ui.state.ratePendingDuration"] = 1,
            ["ui.state.pollingStoppedStale"] = 1,
            ["ui.state.fetchFailedRow"] = 1,
            ["ui.state.fetchFailedBadge"] = 1,
            ["ui.state.loggingUnavailable"] = 1,

            // 14.4 ui.state.* without placeholders
            ["ui.state.pollingStoppedExited"] = 0,
            ["ui.state.leagueUnresolved"] = 0,
            ["ui.state.commitRejected"] = 0,
            ["ui.state.settingsWriteFailed"] = 0,
            ["ui.state.settingsCorrupt"] = 0,
            ["ui.state.settingsReadOnly"] = 0,
            ["ui.state.settingsUnreadable"] = 0,
            ["ui.state.trayUnavailable"] = 0,
            ["ui.state.viewModelRefreshFailing"] = 0,
            ["ui.state.rateInherited"] = 0,
            ["ui.state.itemUnresolved"] = 0,
            ["ui.state.itemDropped"] = 0,

            // 14.5 ui.error.* (catalogue transcribed from 13.6)
            ["ui.error.network"] = 0,
            ["ui.error.timeout"] = 0,
            ["ui.error.httpStatus"] = 1,
            ["ui.error.rateLimited"] = 0,
            ["ui.error.deserialization"] = 0,
            ["ui.error.emptyLines"] = 1,
            ["ui.error.noPricedLines"] = 1,
            ["ui.error.primaryCurrencyMismatch"] = 0,
            ["ui.error.divineLineMissing"] = 0,
            ["ui.error.medianJump"] = 1,
            ["ui.error.leagueListInvalid"] = 0,
            ["ui.error.mappingFault"] = 0,
            ["ui.error.fieldMissingRatio"] = 1,
            ["ui.error.applyFault"] = 0,
            ["ui.error.settingsWriteFailed"] = 0,
            ["ui.error.settingsCorrupt"] = 1,
            ["ui.error.generic"] = 0,

            // 14.6 ui.tray.*
            ["ui.tray.tooltipMore"] = 1,
            ["ui.tray.openSettings"] = 0,
            ["ui.tray.movePositionOff"] = 0,
            ["ui.tray.exit"] = 0,
            ["ui.tray.appName"] = 0,

            // 14.7 ui.footer.*
            ["ui.footer.attribution"] = 0,

            // 18.3 ui.overlay.* (treated as rows of 14.3)
            ["ui.overlay.moreRows"] = 1,
            ["ui.overlay.moreRowsExplicit"] = 1,

            // 14.9 ui.settings.* — the settings window's fixed strings (S3 5.4.4). None takes an
            // argument; they are catalogued so a translator's stray "{0}" is dropped and reported
            // rather than reaching string.Format at render time.
            ["ui.settings.title"] = 0,
            ["ui.settings.firstRun.overflow"] = 0,
            ["ui.settings.firstRun.taskbar"] = 0,
            ["ui.settings.firstRun.dismiss"] = 0,
            ["ui.settings.search.header"] = 0,
            ["ui.settings.search.add"] = 0,
            ["ui.settings.watchlist.header"] = 0,
            ["ui.settings.watchlist.remove"] = 0,
            ["ui.settings.preferences.header"] = 0,
            ["ui.settings.league"] = 0,
            ["ui.settings.leagueInUse"] = 0,
            ["ui.settings.leagueList"] = 0,
            ["ui.settings.leagueReload"] = 0,
            ["ui.settings.retryNow"] = 0,
            ["ui.settings.refreshMinutes"] = 0,
            ["ui.settings.language"] = 0,
            ["ui.settings.displayCurrency"] = 0,
            ["ui.settings.opacity"] = 0,
            ["ui.settings.writesBlocked"] = 0,
            ["ui.settings.placement.header"] = 0,
            ["ui.settings.placement.moveMode"] = 0,
            ["ui.settings.placement.revertHeight"] = 0,
            ["ui.settings.placement.reset"] = 0,
            ["ui.settings.diagnostics.header"] = 0,
            ["ui.settings.diagnostics.openLogFolder"] = 0,
            ["ui.settings.diagnostics.retryTray"] = 0,
            ["ui.settings.diagnostics.acknowledgeCorrupt"] = 0,
            ["ui.settings.diagnostics.recentHeader"] = 0,
            ["ui.settings.closeNotice"] = 0,
            ["ui.settings.search.found"] = 0,
            ["ui.settings.search.notInCache"] = 0,
            ["ui.settings.search.cacheEmpty"] = 0,
            ["ui.settings.search.noPrice"] = 0,
            ["ui.settings.leagueStatus.ok"] = 0,
            ["ui.settings.leagueStatus.suspicious"] = 0,
            ["ui.settings.leagueStatus.failed"] = 0,

            // 14.10 ui.category.* — one per ExchangeCategory member (S4 3.3)
            ["ui.category.currency"] = 0,
            ["ui.category.fragment"] = 0,
            ["ui.category.runegraft"] = 0,
            ["ui.category.allflameEmber"] = 0,
            ["ui.category.tattoo"] = 0,
            ["ui.category.omen"] = 0,
            ["ui.category.djinnCoin"] = 0,
            ["ui.category.ducat"] = 0,
            ["ui.category.enshroudingCrystal"] = 0,
            ["ui.category.divinationCard"] = 0,
            ["ui.category.artifact"] = 0,
            ["ui.category.oil"] = 0,
            ["ui.category.deliriumOrb"] = 0,
            ["ui.category.scarab"] = 0,
            ["ui.category.astrolabe"] = 0,
            ["ui.category.fossil"] = 0,
            ["ui.category.resonator"] = 0,
            ["ui.category.essence"] = 0,

            // 3.2 — a dictionary names itself
            [SelfNameKey] = 0,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every catalogued key, for the parity tests (S2 11.11 C1).</summary>
    public static IReadOnlyCollection<string> Keys => ArgumentCounts.Keys;

    /// <summary>True when <paramref name="key"/> belongs to the <c>ui.*</c> space (S2 3.1).</summary>
    public static bool IsUiKey(string key)
        => key.StartsWith(UiPrefix, StringComparison.Ordinal);

    /// <summary>The argument count the call site passes, when the key is catalogued.</summary>
    public static bool TryGetArgumentCount(string key, out int count)
        => ArgumentCounts.TryGetValue(key, out count);
}
