namespace PoeOverlay.Core.Diagnostics;

/// <summary>
/// What the boot sequence learned before the Store existed to be told (S4 4.6, consumed in S4 12.2).
/// </summary>
/// <remarks>
/// Program.cs fills this in as a local at step 1 (opening the logger) and step 5 (loading
/// settings), then reconciles it exactly once, right after <c>Store.StartAsync</c> completes.
/// </remarks>
public sealed class DiagnosticsStartupState
{
    /// <summary>True when the log file could not be opened; reconciled as LoggingUnavailable.</summary>
    public bool LoggerOpenFailed { get; init; }

    /// <summary>Path of the shutdown flush-failure trace file, or null when there is none.</summary>
    public string? SettingsFlushFailureTracePath { get; init; }
}
