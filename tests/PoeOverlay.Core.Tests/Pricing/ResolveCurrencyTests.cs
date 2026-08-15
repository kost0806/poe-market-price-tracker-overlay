using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Pricing;
using Xunit;

namespace PoeOverlay.Core.Tests.Pricing;

/// <summary>
/// S2 11.3 (R1–R7), FR-04-3 — the distinction the design itself calls subtle: an omitted per-entry
/// preference inherits the global one, an explicit <c>Auto</c> asks for token resolution.
/// </summary>
public sealed class ResolveCurrencyTests
{
    [Fact]
    public void R1_OmittedPreference_InheritsTheGlobalDefaultAndIgnoresTheToken()
        => Assert.Equal(
            ResolvedCurrency.Chaos,
            PricingEngine.Resolve(entryPref: null, DisplayCurrency.Chaos, "divine"));

    [Fact]
    public void R2_ExplicitAuto_IsNotOmission()
        => Assert.Equal(
            ResolvedCurrency.Divine,
            PricingEngine.Resolve(DisplayCurrency.Auto, DisplayCurrency.Chaos, "divine"));

    [Fact]
    public void R3_ExplicitCurrency_NeverLooksAtTheToken()
        => Assert.Equal(
            ResolvedCurrency.Divine,
            PricingEngine.Resolve(DisplayCurrency.Divine, DisplayCurrency.Chaos, "chaos"));

    [Fact]
    public void R4_TokenComparisonIgnoresCase()
        => Assert.Equal(
            ResolvedCurrency.Divine,
            PricingEngine.Resolve(entryPref: null, DisplayCurrency.Auto, "DIVINE"));

    [Fact]
    public void R5_TokenIsTrimmed()
        => Assert.Equal(
            ResolvedCurrency.Chaos,
            PricingEngine.Resolve(entryPref: null, DisplayCurrency.Auto, " chaos "));

    [Fact]
    public void R5Prime_TrimAndCaseTogether_MatchTheMarketSidePredicate()
        => Assert.Equal(
            ResolvedCurrency.Divine,
            PricingEngine.Resolve(entryPref: null, DisplayCurrency.Auto, "  Divine "));

    [Fact]
    public void R6_UnknownToken_FallsBackToChaos()
        => Assert.Equal(
            ResolvedCurrency.Chaos,
            PricingEngine.Resolve(entryPref: null, DisplayCurrency.Auto, "exalted"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void R7_BlankOrMissingToken_FallsBackToChaos(string? token)
        => Assert.Equal(
            ResolvedCurrency.Chaos,
            PricingEngine.Resolve(entryPref: null, DisplayCurrency.Auto, token));

    [Fact]
    public void GlobalAutoWithAnOmittedEntry_StillResolvesTheToken()
        => Assert.Equal(
            ResolvedCurrency.Divine,
            PricingEngine.Resolve(entryPref: null, DisplayCurrency.Auto, "divine"));
}
