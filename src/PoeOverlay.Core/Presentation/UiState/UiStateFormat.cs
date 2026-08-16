using System.Globalization;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Pricing;

namespace PoeOverlay.Core.Presentation.UiState;

/// <summary>
/// Presentation's half of the three-layer template chain (S3 9.3 D-PS8 / S4 11.8 G1).
/// </summary>
internal static class UiStateFormat
{
    /// <summary>
    /// Resolves <paramref name="key"/> and formats it, falling back to
    /// <paramref name="fallbackConst"/> at each of the three layers.
    /// </summary>
    /// <remarks>
    /// The implementation is <c>PricingEngine.Tmpl</c> itself rather than a copy of it: S4 asked for
    /// an isomorphic helper, and two isomorphic helpers are two places for the sentinel rule to
    /// drift. Every argument is already a formatted string (D-PR4).
    /// </remarks>
    public static string Ui(ITemplateSource templates, string key, string fallbackConst, params string[] args)
        => PricingEngine.Tmpl(templates, key, fallbackConst, args);

    /// <summary>
    /// Formats a bare duration as <c>45s</c> / <c>3m</c> / <c>2h</c> / <c>4d</c>.
    /// </summary>
    /// <remarks>
    /// <c>ui.state.ratePendingDuration</c> takes a duration ("3m"), not a relative time
    /// ("3m ago"), and S4 defined no formatter for it — <c>PricingEngine.Relative</c> would render
    /// "rate pending for 3m ago". Truncated rather than rounded, matching <c>Relative</c>, and a
    /// negative span clamps to zero.
    /// </remarks>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span < TimeSpan.FromMinutes(1))
        {
            return Whole(span.TotalSeconds) + "s";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return Whole(span.TotalMinutes) + "m";
        }

        return span < TimeSpan.FromDays(1)
            ? Whole(span.TotalHours) + "h"
            : Whole(span.TotalDays) + "d";
    }

    /// <summary>An invariant integer, so a comma separator cannot appear in a duration.</summary>
    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Whole(double value)
        => ((long)Math.Floor(value)).ToString(CultureInfo.InvariantCulture);
}
