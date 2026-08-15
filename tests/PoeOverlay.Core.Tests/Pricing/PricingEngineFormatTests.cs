using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Pricing;
using PoeOverlay.Core.Tests.Localization;
using Xunit;

namespace PoeOverlay.Core.Tests.Pricing;

/// <summary>
/// S2 11.1 (P1–P14) — the five FR-04-4 rows with <c>r = 194.6</c>, driven through the real embedded
/// English dictionary (S2 11 common rules).
/// </summary>
/// <remarks>
/// <para>
/// Every case asserts the <see cref="PriceForm"/> as well as the string. The eight forms exist so a
/// test pins the branch; asserting only the text would let a dictionary edit rewrite the meaning of
/// a passing test, and would not tell row 2 from a row 1 that lost its rate.
/// </para>
/// <para>
/// P3 uses <c>v = 0.0644</c> and P5 uses <c>v = 0.06401</c>. The specification quotes two different
/// snapshots on purpose; unifying them would stop reproducing one of its two printed examples.
/// </para>
/// </remarks>
public sealed class PricingEngineFormatTests : IDisposable
{
    private static readonly DateTimeOffset Fetched = new(2026, 8, 16, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly LocalizationHarness _harness = LocalizationHarness.Create();
    private readonly LocalizationCatalog _templates;

    public PricingEngineFormatTests() => _templates = _harness.Start();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void P1_ChaosDisplayWithAtLeastOneDivine_PrintsBoth()
        => AssertPrice(359.7m, ResolvedCurrency.Chaos, Rate(), PriceForm.ChaosWithDivine, "359.7c (1.85d)");

    [Fact]
    public void P2_ChaosDisplayUnderOneDivine_PrintsChaosOnly()
        => AssertPrice(43.5m, ResolvedCurrency.Chaos, Rate(), PriceForm.ChaosOnly, "43.5c");

    [Fact]
    public void P3_ChaosDisplayUnderOneChaos_PrintsTheReciprocal()
        => AssertPrice(0.0644m, ResolvedCurrency.Chaos, Rate(), PriceForm.ChaosReciprocal, "15.5 per 1c");

    [Fact]
    public void P3Prime_TheOtherSnapshotOfTheSameItem_PrintsItsOwnReciprocal()
        => AssertPrice(0.06401m, ResolvedCurrency.Chaos, Rate(), PriceForm.ChaosReciprocal, "15.6 per 1c");

    [Fact]
    public void P4_DivineDisplayAtOrAboveOneDivine_PrintsDivine()
        => AssertPrice(359.7m, ResolvedCurrency.Divine, Rate(), PriceForm.DivineOnly, "1.85d");

    [Fact]
    public void P5_DivineDisplayUnderOneDivine_PrintsRateOverValueNotOneOverD()
    {
        // 194.6 / 0.06401 = 3040.1499… → 3,040. Computing 1/(v/r) instead would truncate twice.
        // This case cannot catch the other shortcut — printing maxVolumeRate straight from the
        // response gives the same characters (S2 4.3.6).
        AssertPrice(0.06401m, ResolvedCurrency.Divine, Rate(), PriceForm.DivineReciprocal, "3,040 per 1d");
    }

    [Fact]
    public void P6_ChaosDisplayWithNoRate_SaysSoRatherThanCollapsingToRowTwo()
    {
        // Printing a bare "359.7c" would be character-identical to row 2, and the missing
        // parenthesis would actively assert "under one divine".
        AssertPrice(359.7m, ResolvedCurrency.Chaos, rate: null, PriceForm.ChaosRatePending, "359.7c (rate pending)");
    }

    [Fact]
    public void P7_ChaosReciprocalNeedsNoRate()
        => AssertPrice(0.0644m, ResolvedCurrency.Chaos, rate: null, PriceForm.ChaosReciprocal, "15.5 per 1c");

    [Fact]
    public void P8_DivineDisplayWithNoRate_HasNothingToShow()
        => AssertPrice(359.7m, ResolvedCurrency.Divine, rate: null, PriceForm.RatePending, "rate pending");

    [Fact]
    public void P9_ExactlyOneDivine_KeepsItsTrailingZeros()
    {
        // 194.6m / 194.6m is exactly 1m — the threshold needs no epsilon — and "1.00d" rather than
        // "1d" is what tells the reader which side of the row 4 / row 5 boundary this is.
        AssertPrice(194.6m, ResolvedCurrency.Divine, Rate(), PriceForm.DivineOnly, "1.00d");
    }

    [Fact]
    public void P10_ExactlyOneChaos_TakesTheChaosBranch()
        => AssertPrice(1m, ResolvedCurrency.Chaos, Rate(), PriceForm.ChaosOnly, "1.00c");

    [Fact]
    public void P11_JustUnderOneChaos_TakesTheReciprocalBranch()
        => AssertPrice(0.9999m, ResolvedCurrency.Chaos, Rate(), PriceForm.ChaosReciprocal, "1.00 per 1c");

    [Fact]
    public void P12_ZeroValue_IsUnavailable()
        => AssertPrice(0m, ResolvedCurrency.Chaos, Rate(), PriceForm.Unavailable, "—");

    [Fact]
    public void P12Prime_NegativeValue_IsUnavailable()
        => AssertPrice(-5m, ResolvedCurrency.Chaos, Rate(), PriceForm.Unavailable, "—");

    [Fact]
    public void P12DoublePrime_ValueBelowTheFloor_IsUnavailableInsteadOfOverflowing()
    {
        // Without the MinPrice floor this reaches r / v and 194.6m / 1e-28m throws
        // OverflowException — which would break "Pricing never throws" during binding.
        AssertPrice(1e-12m, ResolvedCurrency.Divine, Rate(), PriceForm.Unavailable, "—");
    }

    [Fact]
    public void P13_ExpiredRate_SuppressesTheDivineFigureEntirely()
    {
        // Expiry is not "under one divine", so the answer is ChaosRatePending, not ChaosOnly (D16).
        var stale = new DivineRate(194.6m, Fetched - TimeSpan.FromMinutes(31), "Standard", Inherited: false);
        AssertPrice(359.7m, ResolvedCurrency.Chaos, stale, PriceForm.ChaosRatePending, "359.7c (rate pending)");
    }

    [Fact]
    public void P14_NonPositiveRate_FailsTheGate()
    {
        var zero = new DivineRate(0m, Fetched, "Standard", Inherited: false);
        AssertPrice(359.7m, ResolvedCurrency.Chaos, zero, PriceForm.ChaosRatePending, "359.7c (rate pending)");
    }

    [Fact]
    public void P14Prime_RateBelowTheFloor_FailsTheGateInsteadOfOverflowing()
    {
        // 194.6m / 1e-28m is the measured OverflowException. The gate's ">0" alone does not stop it.
        var minute = new DivineRate(1e-28m, Fetched, "Standard", Inherited: false);
        AssertPrice(359.7m, ResolvedCurrency.Chaos, minute, PriceForm.ChaosRatePending, "359.7c (rate pending)");
    }

    [Fact]
    public void HugeRateAgainstATinyValue_IsUnavailableRatherThanAnOverflow()
    {
        // Row 5 computes r / v; with r near decimal's ceiling and v at the floor the quotient does
        // not fit. Both operands pass every documented gate.
        var huge = new DivineRate(7e28m, Fetched, "Standard", Inherited: false);
        AssertPrice(1e-9m, ResolvedCurrency.Divine, huge, PriceForm.Unavailable, "—");
    }

    [Fact]
    public void ChaosOnly_InheritsTheRatesAgeAndItsInheritanceMark()
    {
        // The rate is invisible in "43.5c" but decided the branch, so the row is as old as the rate.
        var acquired = Fetched - TimeSpan.FromMinutes(10);
        var inherited = new DivineRate(194.6m, acquired, "Standard", Inherited: true);

        var display = Format(43.5m, ResolvedCurrency.Chaos, inherited);

        Assert.Equal(PriceForm.ChaosOnly, display.Form);
        Assert.True(display.RateInherited);
        Assert.Equal(acquired, display.EffectiveAsOf);
    }

    [Fact]
    public void ChaosReciprocal_IsTheOneRowThatDoesNotInheritTheRate()
    {
        var acquired = Fetched - TimeSpan.FromMinutes(10);
        var inherited = new DivineRate(194.6m, acquired, "Standard", Inherited: true);

        var display = Format(0.0644m, ResolvedCurrency.Chaos, inherited);

        Assert.Equal(PriceForm.ChaosReciprocal, display.Form);
        Assert.False(display.RateInherited);
        Assert.Equal(Fetched, display.EffectiveAsOf);
    }

    [Fact]
    public void EffectiveAsOf_IsTheOlderOfTheFetchAndTheRate()
    {
        var newerRate = new DivineRate(194.6m, Fetched + TimeSpan.FromMinutes(5), "Standard", Inherited: false);

        var display = Format(359.7m, ResolvedCurrency.Chaos, newerRate);

        Assert.Equal(PriceForm.ChaosWithDivine, display.Form);
        Assert.Equal(Fetched, display.EffectiveAsOf);
    }

    [Fact]
    public void RatePendingForms_CarryNoInheritanceMark()
    {
        var stale = new DivineRate(194.6m, Fetched - TimeSpan.FromHours(4), "Standard", Inherited: true);

        var display = Format(359.7m, ResolvedCurrency.Chaos, stale);

        Assert.Equal(PriceForm.ChaosRatePending, display.Form);
        Assert.False(display.RateInherited);
        Assert.Equal(Fetched, display.EffectiveAsOf);
    }

    private void AssertPrice(
        decimal value,
        ResolvedCurrency display,
        DivineRate? rate,
        PriceForm expectedForm,
        string expectedText)
    {
        var result = Format(value, display, rate);

        Assert.Equal(expectedForm, result.Form);
        Assert.Equal(expectedText, result.Text);
    }

    private PriceDisplay Format(decimal value, ResolvedCurrency display, DivineRate? rate)
        => PricingEngine.Format(Price(value), rate, display, Fetched, Fetched, MaxAge, _templates);

    private static DivineRate Rate() => new(194.6m, Fetched, "Standard", Inherited: false);

    private static ItemPrice Price(decimal value)
        => new(
            new ItemId("divine-orb"),
            "Divine Orb",
            value,
            VolumePrimaryValue: null,
            MaxVolumeCurrency: null,
            MaxVolumeRate: null,
            TotalChangePercent: null,
            SelfReportedCategory: null);
}
