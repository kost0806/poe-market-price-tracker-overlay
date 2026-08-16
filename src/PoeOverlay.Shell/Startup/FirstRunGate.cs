using PoeOverlay.Core.Settings;

namespace PoeOverlay.Startup;

/// <summary>
/// Whether the first-run guidance is still owed (FR-08-6 / S4 12.5 B5).
/// </summary>
/// <remarks>
/// The test is the flag's absence, not the settings file's absence. A user who deletes
/// <c>settings.json</c> and a user upgrading from a schema without the flag both still meet the
/// measured problem the guidance exists for — Windows 11 files a freshly registered tray icon into
/// the overflow flyout, two clicks away (<c>00-shell-measurements.md</c> §4.1) — so neither is
/// worth excepting (S3 6.5 P2).
/// </remarks>
internal static class FirstRunGate
{
    /// <summary>True when the settings window should be opened unprompted at start-up.</summary>
    /// <param name="settings">The loaded settings.</param>
    /// <returns>True when the guidance has not been acknowledged.</returns>
    internal static bool ShouldAutoShowSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !settings.FirstRunAcknowledged;
    }
}
