namespace PoeOverlay.Core.Domain;

/// <summary>
/// Per-category tally of lines dropped during mapping (S2 2.6 / S4 3.5).
/// </summary>
public readonly record struct SkipCounts(int BlankId, int NonPositiveValue, int Duplicate, int ElementFault)
{
    /// <summary>Sum of the four fields; the numerator of the FieldMissingRatio gate.</summary>
    public int Total => BlankId + NonPositiveValue + Duplicate + ElementFault;
}
