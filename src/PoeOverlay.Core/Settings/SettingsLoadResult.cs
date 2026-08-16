namespace PoeOverlay.Core.Settings;

/// <summary>
/// The six outcomes of reading <c>settings.json</c> (S2 8.4 / S4 10.4).
/// </summary>
/// <remarks>
/// A closed record hierarchy rather than a status enum plus nullable payloads: each case carries
/// exactly the data that case has, and a <c>switch</c> over it is checked for exhaustiveness.
/// </remarks>
public abstract record SettingsLoadResult
{
    private SettingsLoadResult()
    {
    }

    /// <summary>The file parsed. <paramref name="Corrections"/> lists the keys that were clamped, defaulted or discarded.</summary>
    public sealed record Loaded(AppSettings Settings, IReadOnlyList<string> Corrections) : SettingsLoadResult;

    /// <summary>No file to read. The normal first-run path; the file appears on the first write.</summary>
    /// <param name="ReasonCode">Only one literal exists: <see cref="SettingsStore.NoFileReason"/>.</param>
    public sealed record Defaulted(string ReasonCode) : SettingsLoadResult;

    /// <summary>The file exists but could not be read. Nothing was quarantined, because nothing could be touched.</summary>
    public sealed record IoFailed(string Path, string ExceptionType) : SettingsLoadResult;

    /// <summary>The file was not JSON, or its root was not an object. It has been moved to <paramref name="QuarantinePath"/>.</summary>
    public sealed record Corrupt(string QuarantinePath) : SettingsLoadResult;

    /// <summary><c>schemaVersion</c> is from the future. What could be read is still returned, so the user keeps seeing their data.</summary>
    public sealed record ReadOnly(AppSettings Settings) : SettingsLoadResult;
}
