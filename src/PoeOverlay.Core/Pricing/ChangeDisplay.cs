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
/// translator supplying a broken glyph would destroy meaning, not translate it. It is the sign on
/// its own, for a caller that wants it separately; it is not something to draw beside
/// <paramref name="Text"/>.
/// </param>
/// <param name="Text">
/// The whole rendered change — <c>ui.price.change</c> is <c>{0}{1}%</c> and Pricing passes the
/// glyph as <c>{0}</c>, so this <em>already begins with</em> <paramref name="Glyph"/>. Empty for
/// <see cref="ChangeDirection.Unknown"/>. A view binds this alone: binding the glyph beside it drew
/// every arrow twice, observed on screen as <c>▼ ▼5.0%</c> (S3 4.8).
/// </param>
public sealed record ChangeDisplay(ChangeDirection Direction, string Glyph, string Text);
