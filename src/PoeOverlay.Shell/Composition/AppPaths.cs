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
/// <param name="IconDirectory">Holds the item icons and their manifest (FR-04-6, HLD D23).</param>
/// <param name="CatalogDirectory">Holds the shipped item catalogue (FR-01-1, contract §6.8).</param>
internal sealed record AppPaths(
    string AppDataDirectory,
    string LogDirectory,
    string LocalizationDirectory,
    string IconDirectory,
    string CatalogDirectory)
{
    /// <summary>Builds the production paths under <c>%APPDATA%</c> and the executable's folder.</summary>
    /// <returns>The resolved paths. Directories are created if missing.</returns>
    /// <remarks>
    /// The icon folder is <em>not</em> created: it is shipped content, and creating an empty one
    /// would turn "the build did not copy the icons" into a folder that merely looks right.
    /// </remarks>
    internal static AppPaths CreateDefault()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ShellConstants.AppDataFolderName);
        var logs = Path.Combine(appData, ShellConstants.LogFolderName);
        var localization = Path.Combine(AppContext.BaseDirectory, "Localization");
        var icons = Path.Combine(AppContext.BaseDirectory, ShellConstants.IconFolderName);
        var catalog = Path.Combine(AppContext.BaseDirectory, ShellConstants.CatalogFolderName);

        _ = Directory.CreateDirectory(appData);
        _ = Directory.CreateDirectory(logs);

        return new AppPaths(appData, logs, localization, icons, catalog);
    }
}
