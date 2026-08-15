namespace PoeOverlay.Core.Domain;

/// <summary>One entry of the condition map (S2 2.11 / S4 3.8).</summary>
public sealed record ConditionState(bool Active, DateTimeOffset Since, string? Detail);
