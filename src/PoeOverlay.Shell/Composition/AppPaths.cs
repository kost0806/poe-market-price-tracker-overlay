using System.IO;

namespace PoeOverlay.Composition;

/// <summary>
/// Where the application keeps its files.
/// </summary>
/// <remarks>
/// A value rather than direct <c>Environment</c> reads at each site, so a test — and the probe
/// harness — can point a whole composition at a temporary folder.
/// </remarks>
/// <param name="AppDataDirectory">Holds <c>settings.json</c>, its backup, and the flush-failure trace.</param>
/// <param name="LogDirectory">Holds the rolling log files.</param>
/// <param name="LocalizationDirectory">Holds the discovered dictionaries (S2 3.2).</param>
internal sealed record AppPaths(string AppDataDirectory, string LogDirectory, string LocalizationDirectory)
{
    /// <summary>Builds the production paths under <c>%APPDATA%</c> and the executable's folder.</summary>
    /// <returns>The resolved paths. Directories are created if missing.</returns>
    internal static AppPaths CreateDefault()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ShellConstants.AppDataFolderName);
        var logs = Path.Combine(appData, ShellConstants.LogFolderName);
        var localization = Path.Combine(AppContext.BaseDirectory, "Localization");

        _ = Directory.CreateDirectory(appData);
        _ = Directory.CreateDirectory(logs);

        return new AppPaths(appData, logs, localization);
    }
}
