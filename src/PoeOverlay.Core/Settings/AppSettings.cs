using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Settings;

/// <summary>
/// Everything the application persists, as one immutable value (S2 8.1 / S4 10.1).
/// </summary>
/// <remarks>
/// The declared type of <see cref="Watchlist"/> is <see cref="EquatableArray{T}"/> and that is
/// load-bearing. Widening it back to <c>IReadOnlyList&lt;WatchlistEntry&gt;</c> compiles cleanly
/// and silently breaks record equality, after which every save raises <c>Changed</c>, Polling
/// bumps its round generation, cancels the round in flight and schedules a repoll — an infinite
/// re-entry with no compiler error anywhere (S2 8.3 D-D2).
/// </remarks>
/// <param name="SchemaVersion">1. A higher value on disk puts the store into read-only mode.</param>
/// <param name="League">A user-entered league id, or null for "resolve from the league list".</param>
/// <param name="DefaultDisplayCurrency">The fallback for entries that omit their own.</param>
/// <param name="FirstRunAcknowledged">FR-08-6 — false until the user dismisses the first-run banner.</param>
public sealed record AppSettings(
    int SchemaVersion,
    string? League,
    int RefreshIntervalMinutes,
    string Language,
    DisplayCurrency DefaultDisplayCurrency,
    WindowSettings Window,
    EquatableArray<WatchlistEntry> Watchlist,
    bool FirstRunAcknowledged)
{
    /// <summary>The value a first run starts from; matches the S4 15 default table.</summary>
    public static AppSettings Default { get; } = new(
        SettingsValidation.CurrentSchemaVersion,
        null,
        SettingsValidation.DefaultRefreshIntervalMinutes,
        SettingsValidation.DefaultLanguage,
        DisplayCurrency.Auto,
        WindowSettings.Default,
        new EquatableArray<WatchlistEntry>([]),
        false);
}
