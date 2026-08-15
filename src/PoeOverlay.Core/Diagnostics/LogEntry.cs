using Microsoft.Extensions.Logging;

namespace PoeOverlay.Core.Diagnostics;

/// <summary>
/// One log line, before formatting (S2 9.1 / S4 4.1).
/// </summary>
/// <remarks>
/// Diagnostics references no other module, not even Domain, so every field is a primitive —
/// <see cref="Category"/> is a string rather than <c>ExchangeCategory</c> on purpose (S2 1.2).
/// <para>
/// <see cref="RoundNumber"/> is empty on commit-rejection warnings, because the Store only ever
/// receives a DataTag (D-ST1). Correlating a rejection with its round needs a Debug line emitted
/// by Polling just before the commit.
/// </para>
/// </remarks>
public sealed record LogEntry(
    DateTimeOffset At,
    LogLevel Level,
    string Module,
    string Message,
    string? League,
    int? DataEpoch,
    int? RoundNumber,
    string? Category,
    string? Code,
    string? ExceptionType);
