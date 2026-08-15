using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Pricing;

/// <summary>
/// A formatted change percentage (S2 4.4.1 / S4 6.1).
/// </summary>
/// <param name="Direction">
/// What the View selects brush and visibility from. Distinguishing <c>Flat</c> from <c>Unknown</c>
/// by comparing <paramref name="Text"/> would make the trigger a string comparison, which breaks on
/// a language change.
/// </param>
/// <param name="Glyph">
/// <c>▲</c>, <c>▼</c> or empty. A compile-time constant of Pricing, never a dictionary key — a
/// translator supplying a broken glyph would destroy meaning, not translate it.
/// </param>
/// <param name="Text">
/// The magnitude, always absolute; the sign is carried by <paramref name="Glyph"/>. Empty for
/// <see cref="ChangeDirection.Unknown"/>.
/// </param>
public sealed record ChangeDisplay(ChangeDirection Direction, string Glyph, string Text);
