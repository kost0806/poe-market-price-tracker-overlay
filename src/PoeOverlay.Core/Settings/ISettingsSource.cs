namespace PoeOverlay.Core.Settings;

/// <summary>
/// Carries both values so that each consumer diffs only the keys it cares about (S2 8.3).
/// </summary>
public delegate void SettingsChangedHandler(AppSettings oldSettings, AppSettings newSettings);

/// <summary>
/// The settings surface every other module sees (S4 10.3).
/// </summary>
/// <remarks>
/// <see cref="Update"/> takes a <em>value</em>, never a <c>Func&lt;AppSettings, AppSettings&gt;</c>.
/// A delegate could read the live window inside itself, and while <c>SizeToContent="Height"</c> is
/// active the window's Height is whatever the last layout pass produced rather than what the user
/// chose (measured: 500 → 136 → 680 → 300 → 102 → 68). The type forbids the mistake instead of a
/// convention asking for it (S2 8.6, D19).
/// </remarks>
public interface ISettingsSource
{
    /// <summary>The current value. Never null.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after <see cref="Current"/> has been published, and only when the value actually changed.</summary>
    event SettingsChangedHandler? Changed;

    /// <summary>Replaces the current value, notifies, and schedules a debounced write.</summary>
    void Update(AppSettings next);

    /// <summary>Writes any pending value now. Idempotent, callable from any path, and a no-op when nothing is pending.</summary>
    Task FlushAsync(CancellationToken ct);

    /// <summary>Clears a <see cref="WriteBlockReason.Corrupt"/> block and immediately persists the in-memory state. Refused for the other two reasons.</summary>
    void Acknowledge();

    /// <summary>Why writes are blocked, or <see cref="WriteBlockReason.None"/>.</summary>
    WriteBlockReason BlockReason { get; }
}
