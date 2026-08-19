using System.Globalization;
using PoeOverlay.Core.Pricing;
using Xunit;

namespace PoeOverlay.Core.Tests.Pricing;

/// <summary>
/// S2 11.2 — the measured band table, reproduced. Band is chosen before rounding, grouping after.
/// </summary>
public sealed class NumberFormatterTests
{
    [Theory]
    [InlineData("3040.1499", "3,040")]      // ≥ 1000 → no decimals, grouped
    [InlineData("999.96", "1,000.0")]       // band before rounding, grouping after
    [InlineData("1.845", "1.85")]           // AwayFromZero; ToEven would give 1.84
    [InlineData("15.6226", "15.6")]
    [InlineData("1", "1.00")]               // trailing zeros are kept
    [InlineData("1000000", "1,000,000")]
    [InlineData("0.5", "0.500")]            // below the declared domain: formatted, never thrown
    public void Num_ReproducesTheMeasuredBandTable(string input, string expected)
        => Assert.Equal(expected, NumberFormatter.Num(decimal.Parse(input, CultureInfo.InvariantCulture)));

    [Fact]
    public void Num_UsesTheInvariantCultureRegardlessOfTheCallingThread()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A culture whose separators are the other way round.
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("3,040", NumberFormatter.Num(3040.1499m));
            Assert.Equal("1.85", NumberFormatter.Num(1.845m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Num_AtTheBandBoundaries_PicksTheBandOfTheUnroundedValue()
    {
        Assert.Equal("9.99", NumberFormatter.Num(9.99m));

        // Chosen from the unrounded value, and never re-chosen: 9.995 is a two-decimal value even
        // though rounding lifts it into the next band.
        Assert.Equal("10.00", NumberFormatter.Num(9.995m));
        Assert.Equal("999.9", NumberFormatter.Num(999.94m));
        Assert.Equal("1,000", NumberFormatter.Num(1000m));
    }

    [Fact]
    public void MinPrice_IsTheDocumentedFloor()
        => Assert.Equal(1e-9m, NumberFormatter.MinPrice);
}
