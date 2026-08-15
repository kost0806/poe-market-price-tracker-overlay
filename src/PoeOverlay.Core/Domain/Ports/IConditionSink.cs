namespace PoeOverlay.Core.Domain.Ports;

/// <summary>
/// Dependency-inverted way for Settings and Shell to raise conditions without knowing the Store
/// (S2 2.13 D-C5 / S4 3.9).
/// </summary>
/// <remarks>The Store is the only implementation. Synchronous and immediate — it writes a command to a channel.</remarks>
public interface IConditionSink
{
    /// <summary>Sets or clears a condition. Returns immediately; never awaits.</summary>
    void Set(AppConditionKind kind, bool active, string? detail);
}
