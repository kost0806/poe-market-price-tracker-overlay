using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Pricing;
using PoeOverlay.Core.Tests.Localization;
using Xunit;

namespace PoeOverlay.Core.Tests.Pricing;

/// <summary>
/// S2 11.4 — direction, glyph and magnitude, including the <c>0.05</c> boundary (D-PR5) and the
/// <c>1e300</c> case that the first edition's decimal cast turned into an exception.
/// </summary>
/// <remarks>
/// The direction is asserted alongside the text because the View picks brush and visibility from
/// the enum; telling <c>Flat</c> from <c>Unknown</c> by comparing strings breaks on a language
/// change.
/// </remarks>
public sealed class ChangeDisplayTests : IDisposable
{
    private readonly LocalizationHarness _harness = LocalizationHarness.Create();
    private readonly LocalizationCatalog _templates;

    public ChangeDisplayTests() => _templates = _harness.Start();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Rise_CarriesTheUpGlyph()
        => AssertChange(30.46, ChangeDirection.Up, "▲", "▲30.5%");

    [Fact]
    public void Fall_PrintsTheMagnitudeAndLetsTheGlyphCarryTheSign()
        => AssertChange(-6.2, ChangeDirection.Down, "▼", "▼6.2%");

    [Fact]
    public void JustInsideTheDeadZone_IsFlat()
        => AssertChange(0.049, ChangeDirection.Flat, "", "0.0%");

    [Fact]
    public void AtTheBoundary_TheVerdictFollowsTheRounding()
    {
        // The inequality and the "rounds to 0.0%" note in HLD 6.3 disagree exactly here. Taking the
        // verdict after rounding makes "a glyph implies a non-zero number" true by construction.
        AssertChange(0.05, ChangeDirection.Up, "▲", "▲0.1%");
    }

    [Fact]
    public void SmallFall_LosesItsSignOnPurpose()
    {
        // The dead zone is a finding of "no direction", not of "a small fall".
        AssertChange(-0.03, ChangeDirection.Flat, "", "0.0%");
    }

    [Fact]
    public void MissingValue_IsUnknownWithAnEmptyText()
        => AssertChange(null, ChangeDirection.Unknown, "", "");

    [Fact]
    public void EnormousValue_DoesNotThrow()
    {
        var change = PricingEngine.Change(1e300, _templates);

        Assert.Equal(ChangeDirection.Up, change.Direction);
        Assert.StartsWith("▲1,000,000,000", change.Text, StringComparison.Ordinal);
        Assert.EndsWith(".0%", change.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NotANumber_IsUnknown()
        => AssertChange(double.NaN, ChangeDirection.Unknown, "", "");

    [Fact]
    public void Infinity_IsUnknown()
    {
        AssertChange(double.PositiveInfinity, ChangeDirection.Unknown, "", "");
        AssertChange(double.NegativeInfinity, ChangeDirection.Unknown, "", "");
    }

    [Fact]
    public void ExactZero_IsFlat()
        => AssertChange(0d, ChangeDirection.Flat, "", "0.0%");

    private void AssertChange(double? input, ChangeDirection direction, string glyph, string text)
    {
        var change = PricingEngine.Change(input, _templates);

        Assert.Equal(direction, change.Direction);
        Assert.Equal(glyph, change.Glyph);
        Assert.Equal(text, change.Text);
    }
}
