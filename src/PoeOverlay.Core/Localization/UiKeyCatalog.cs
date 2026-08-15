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
