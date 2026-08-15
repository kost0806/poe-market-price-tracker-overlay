namespace PoeOverlay.Core.Pricing;

/// <summary>
/// Compile-time copies of every template <c>Pricing</c> renders (S2 4.6.2 / S4 6.3).
/// </summary>
/// <remarks>
/// <para>
/// The fallback chain ends by returning the key itself, which is a diagnostic for a state string and
/// a loss of function for a price: <c>string.Format("ui.price.chaos", "43.5")</c> renders
/// <c>ui.price.chaos</c> and the number is gone — arguments passed to a template with no
/// placeholders are silently discarded.
/// </para>
/// <para>
/// Every value here must equal the embedded <c>en.json</c> entry character for character; the
/// parity test (S2 11.11 C1) is what enforces it, so adding a key without a constant fails the
/// build's test run.
/// </para>
/// </remarks>
internal static class PriceTemplates
{
    /// <summary>ui.price.chaos, 1 argument.</summary>
    public const string Chaos = "{0}c";

    /// <summary>ui.price.divine, 1 argument.</summary>
    public const string Divine = "{0}d";

    /// <summary>ui.price.chaosWithDivine, 2 arguments.</summary>
    public const string ChaosWithDivine = "{0}c ({1}d)";

    /// <summary>
    /// ui.price.chaosRatePending, 1 argument. A distinct form because collapsing it to
    /// <see cref="Chaos"/> would print <c>359.7c</c>, identical to row 2, where the absence of the
    /// parenthesis actively asserts "less than one divine" (S2 4.2 point 2).
    /// </summary>
    public const string ChaosRatePending = "{0}c (rate pending)";

    /// <summary>ui.price.perChaos, 1 argument.</summary>
    public const string PerChaos = "{0} per 1c";

    /// <summary>ui.price.perDivine, 1 argument. Korean moves the argument, hence a template.</summary>
    public const string PerDivine = "{0} per 1d";

    /// <summary>ui.price.ratePending, 0 arguments.</summary>
    public const string RatePending = "rate pending";

    /// <summary>ui.price.unavailable, 0 arguments. Em dash, U+2014 (S4 14.1).</summary>
    public const string Unavailable = "\u2014";

    /// <summary>ui.price.change, 2 arguments: glyph then absolute magnitude.</summary>
    public const string Change = "{0}{1}%";

    /// <summary>ui.time.justNow, 0 arguments.</summary>
    public const string JustNow = "just now";

    /// <summary>ui.time.secondsAgo, 1 argument.</summary>
    public const string SecondsAgo = "{0}s ago";

    /// <summary>ui.time.minutesAgo, 1 argument.</summary>
    public const string MinutesAgo = "{0}m ago";

    /// <summary>ui.time.hoursAgo, 1 argument.</summary>
    public const string HoursAgo = "{0}h ago";

    /// <summary>ui.time.daysAgo, 1 argument.</summary>
    public const string DaysAgo = "{0}d ago";
}

/// <summary>The localization keys <see cref="PriceTemplates"/> mirrors (S4 14.1, 14.2).</summary>
internal static class PriceKeys
{
    /// <summary>Key of <see cref="PriceTemplates.Chaos"/>.</summary>
    public const string Chaos = "ui.price.chaos";

    /// <summary>Key of <see cref="PriceTemplates.Divine"/>.</summary>
    public const string Divine = "ui.price.divine";

    /// <summary>Key of <see cref="PriceTemplates.ChaosWithDivine"/>.</summary>
    public const string ChaosWithDivine = "ui.price.chaosWithDivine";

    /// <summary>Key of <see cref="PriceTemplates.ChaosRatePending"/>.</summary>
    public const string ChaosRatePending = "ui.price.chaosRatePending";

    /// <summary>Key of <see cref="PriceTemplates.PerChaos"/>.</summary>
    public const string PerChaos = "ui.price.perChaos";

    /// <summary>Key of <see cref="PriceTemplates.PerDivine"/>.</summary>
    public const string PerDivine = "ui.price.perDivine";

    /// <summary>Key of <see cref="PriceTemplates.RatePending"/>.</summary>
    public const string RatePending = "ui.price.ratePending";

    /// <summary>Key of <see cref="PriceTemplates.Unavailable"/>.</summary>
    public const string Unavailable = "ui.price.unavailable";

    /// <summary>Key of <see cref="PriceTemplates.Change"/>.</summary>
    public const string Change = "ui.price.change";

    /// <summary>Key of <see cref="PriceTemplates.JustNow"/>.</summary>
    public const string JustNow = "ui.time.justNow";

    /// <summary>Key of <see cref="PriceTemplates.SecondsAgo"/>.</summary>
    public const string SecondsAgo = "ui.time.secondsAgo";

    /// <summary>Key of <see cref="PriceTemplates.MinutesAgo"/>.</summary>
    public const string MinutesAgo = "ui.time.minutesAgo";

    /// <summary>Key of <see cref="PriceTemplates.HoursAgo"/>.</summary>
    public const string HoursAgo = "ui.time.hoursAgo";

    /// <summary>Key of <see cref="PriceTemplates.DaysAgo"/>.</summary>
    public const string DaysAgo = "ui.time.daysAgo";
}
