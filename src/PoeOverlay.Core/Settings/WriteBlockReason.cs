namespace PoeOverlay.Core.Settings;

/// <summary>
/// Why the settings file may not be written this session (S2 8.7 D-SE2 / S4 10.3).
/// </summary>
/// <remarks>
/// One boolean behind three different events would leave no way to write the release rule:
/// <see cref="Corrupt"/> clears on user acknowledgement, the other two only on a later start-up.
/// Acknowledging <see cref="Unreadable"/> would overwrite a file that could not be read — the
/// exact user data that blocking writes exists to protect — and acknowledging
/// <see cref="FutureSchema"/> would overwrite a newer file with an older format.
/// </remarks>
public enum WriteBlockReason
{
    /// <summary>Writes are permitted.</summary>
    None,

    /// <summary>The file failed to parse and was quarantined. The user may acknowledge.</summary>
    Corrupt,

    /// <summary>The file could not be read at all, so it could not be quarantined either.</summary>
    Unreadable,

    /// <summary><c>schemaVersion</c> is greater than this build understands.</summary>
    FutureSchema,
}
