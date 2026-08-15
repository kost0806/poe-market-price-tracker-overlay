using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Pricing;

/// <summary>
/// One formatted price (S2 10.4 / S4 6.1).
/// </summary>
/// <param name="Form">
/// The branch that produced <paramref name="Text"/>. Exposed so tests pin the decision rather than
/// the wording, and survive a dictionary change (S2 4.2).
/// </param>
/// <param name="Text">Never null — <see cref="PriceForm.Unavailable"/> carries the em dash.</param>
/// <param name="EffectiveAsOf">
/// <c>min(fetchedAt, rate.AcquiredAt)</c> for the four forms that depend on the rate, otherwise the
/// category's fetch time (S2 4.5.1). <c>ChaosOnly</c> is in the first group even though no divine
/// figure appears in its text: reaching that form <em>is</em> the product of a <c>d &lt; 1</c>
/// judgement, and that judgement used the rate.
/// </param>
/// <param name="RateInherited">
/// True when the judgement above used a carried-over rate. Pricing supplies the fact and no
/// decoration: no colour, no icon and no suffix — <c>359.7c (1.85d, inherited)</c> would widen the
/// forms against each other and push the price itself off the narrowest surface (D-PR6).
/// </param>
public sealed record PriceDisplay(
    PriceForm Form,
    string Text,
    DateTimeOffset EffectiveAsOf,
    bool RateInherited);
