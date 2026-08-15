namespace PoeOverlay.Core.Domain;

/// <summary>
/// Slug identifier of a priced item (S2 2.1 / S4 3.1).
/// </summary>
/// <remarks>
/// Invariants are enforced by producers (the Market mapper, the Settings validator), never by
/// this type (D-D0). Comparison is ordinal and case sensitive. The value is never normalised.
/// <para>
/// A struct cannot forbid <c>default</c> (S2 1.6), so <c>default(ItemId)</c> must be a defined,
/// harmless state: <see cref="ToString"/> never returns null and <see cref="IsEmpty"/> is true.
/// </para>
/// </remarks>
public readonly record struct ItemId(string Value)
{
    /// <summary>Never returns null, even for <c>default(ItemId)</c> (object.ToString contract).</summary>
    public override string ToString() => Value ?? string.Empty;

    /// <summary>True for <c>default(ItemId)</c> and for blank values.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>Producer-only factory. Does not trim or otherwise normalise.</summary>
    public static bool TryCreate(string? raw, out ItemId id)
    {
        id = new ItemId(raw ?? string.Empty);
        return !id.IsEmpty;
    }
}
