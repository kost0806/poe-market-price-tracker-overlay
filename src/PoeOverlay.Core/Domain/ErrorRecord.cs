namespace PoeOverlay.Core.Domain;

/// <summary>
/// The one error currently worth putting on the banner (S2 2.12 / S4 3.8).
/// </summary>
/// <remarks>
/// Distinct from the Diagnostics recent-error ring, which holds <c>LogEntry</c> values for the
/// settings window (S2 9.3).
/// <para>
/// It carries a <paramref name="MessageKey"/> rather than a display string: the overlay banner
/// and the tray tooltip render this value, and an English literal here would violate FR-07-1.
/// No <see cref="Exception"/> object is carried — that would give Domain a lifetime and
/// serialisation problem.
/// </para>
/// </remarks>
/// <param name="Module">Exactly one of "Polling", "Market", "Settings", "Store", "Shell" (case fixed).</param>
/// <param name="Code">Same string space as <see cref="FailureRecord.Code"/>.</param>
/// <param name="MessageKey">A <c>ui.error.*</c> key, resolved by Localization at display time.</param>
/// <param name="Detail">A short formatted helper string (path, category name). Not translated.</param>
public sealed record ErrorRecord(
    DateTimeOffset At,
    string Module,
    string Code,
    string MessageKey,
    string? Detail,
    string? Category,
    string? League,
    int? RoundNumber,
    string? ExceptionType);
