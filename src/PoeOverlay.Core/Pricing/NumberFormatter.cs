using System.Globalization;

namespace PoeOverlay.Core.Pricing;

/// <summary>
/// The band table, the rounding mode and the fixed culture (S2 4.3 / S4 6.2).
/// </summary>
/// <remarks>
/// Formatting is Pricing's responsibility precisely so that it does not depend on a dictionary
/// lookup succeeding (D3): digits, rounding, separators and glyph belong here, and only the
/// surrounding words and the argument <em>positions</em> belong to Localization.
/// </remarks>
internal static class NumberFormatter
{
    /// <summary>
    /// The floor below which a price is not a price (D-PR8, S4 15.3).
    /// </summary>
    /// <remarks>
    /// <c>PrimaryValue &gt; 0</c> has no lower bound, and <c>194.6m / 1e-28m</c> throws
    /// <see cref="OverflowException"/>. Row 5 (and row 3's <c>1/v</c>) can genuinely reach that
    /// division, which would break "Pricing never throws" and pollute D12's allow-list. No real
    /// listing is worth <c>1e-9c</c>, so answering <c>Unavailable</c> is the honest reading.
    /// </remarks>
    public const decimal MinPrice = 1e-9m;

    /// <summary>
    /// Formats a magnitude. Domain is <c>[1, ∞)</c> (D-PR1); anything below is formatted with three
    /// decimals rather than throwing, because a release build must not throw here.
    /// </summary>
    /// <remarks>
    /// The band is chosen from the value <em>before</em> rounding and never re-chosen afterwards, so
    /// <c>999.96</c> is a one-decimal value and becomes <c>1,000.0</c>. Trailing zeros are kept:
    /// <c>1.00d</c> shrunk to <c>1d</c> reads as an integer and hides which side of the row 4 / row 5
    /// boundary the value sits on.
    /// </remarks>
    public static string Num(decimal x)
    {
        // Rounding is done explicitly, not left to the formatter, so the midpoint rule is ours:
        // ToEven turns 1.845 into 1.84, which is reported as a bug by anyone checking by hand.
        if (x >= 1000m)
        {
            return decimal.Round(x, 0, MidpointRounding.AwayFromZero)
                .ToString("N0", CultureInfo.InvariantCulture);
        }

        if (x >= 10m)
        {
            return decimal.Round(x, 1, MidpointRounding.AwayFromZero)
                .ToString("N1", CultureInfo.InvariantCulture);
        }

        if (x >= 1m)
        {
            return decimal.Round(x, 2, MidpointRounding.AwayFromZero)
                .ToString("N2", CultureInfo.InvariantCulture);
        }

        // Contract violation (D-PR1). Formatted, not thrown.
        return decimal.Round(x, 3, MidpointRounding.AwayFromZero)
            .ToString("N3", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a percentage magnitude, always as <c>double</c> (S2 4.4.2).
    /// </summary>
    /// <remarks>
    /// The <c>decimal</c> cast that used to live here throws on <c>1e30</c> and <c>1e300</c>, both of
    /// which pass the <see cref="double.IsFinite"/> guard. <see cref="Math.Round(double, int, MidpointRounding)"/>
    /// does not throw. There is no band table — a change percentage's magnitude carries no meaning.
    /// </remarks>
    public static string Pct(double x)
        => Math.Round(Math.Abs(x), 1, MidpointRounding.AwayFromZero)
            .ToString("N1", CultureInfo.InvariantCulture);
}
