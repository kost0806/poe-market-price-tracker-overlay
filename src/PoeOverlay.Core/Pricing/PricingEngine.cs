using System.Collections.Concurrent;
using System.Globalization;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;

namespace PoeOverlay.Core.Pricing;

/// <summary>
/// The whole of pricing: currency resolution, the five FR-04-4 rows, change direction and relative
/// time (S2 4 / S4 6.1).
/// </summary>
/// <remarks>
/// <para>
/// Stateless, clockless, and it never throws. There is nowhere to put a <c>TimeProvider</c> — every
/// instant arrives as an argument, and one render pass shares one <c>now</c> so that the first and
/// last rows cannot disagree about whether the rate expired (D-PR7).
/// </para>
/// <para>
/// "Never throws" is load-bearing rather than decorative: this code runs during binding, and an
/// exception there pollutes D12's allow-list. Every impossible input answers
/// <see cref="PriceForm.Unavailable"/>.
/// </para>
/// </remarks>
public static class PricingEngine
{
    private static readonly TimeSpan JustNowWindow = TimeSpan.FromSeconds(10);

    // One entry per (argument count, template). Templates number in the dozens, so the render path
    // pays nothing after the first pass (S2 4.6.2 ②).
    private static readonly ConcurrentDictionary<(int Count, string Template), bool> SentinelCache = new();

    /// <summary>
    /// Resolves which currency a row is displayed in (S2 4.1, FR-04-3).
    /// </summary>
    /// <param name="entryPref">The per-entry preference. <c>null</c> means "omitted", not "auto".</param>
    /// <param name="globalDefault">The global setting, used when the entry omits its own.</param>
    /// <param name="token">The listing's <c>maxVolumeCurrency</c>, consulted only under <c>Auto</c>.</param>
    /// <remarks>
    /// An explicit <c>Auto</c> on the entry is not the same as omission: it asks for token
    /// resolution even when the global default is <c>Chaos</c>. An unknown token falls back to chaos
    /// because <c>core.primary</c> is the only unit the response guarantees. Nothing is logged here —
    /// <c>Market</c> records the unknown token once per session at mapping time (D-C4), and both
    /// sides must judge with <c>Trim()</c> plus <c>OrdinalIgnoreCase</c> or the same token is normal
    /// on one side and unknown on the other.
    /// </remarks>
    public static ResolvedCurrency Resolve(
        DisplayCurrency? entryPref,
        DisplayCurrency globalDefault,
        string? token)
    {
        var preference = entryPref ?? globalDefault;
        if (preference == DisplayCurrency.Chaos)
        {
            return ResolvedCurrency.Chaos;
        }

        if (preference == DisplayCurrency.Divine)
        {
            return ResolvedCurrency.Divine;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return ResolvedCurrency.Chaos;
        }

        var trimmed = token.Trim();
        if (string.Equals(trimmed, "divine", StringComparison.OrdinalIgnoreCase))
        {
            return ResolvedCurrency.Divine;
        }

        return ResolvedCurrency.Chaos;
    }

    /// <summary>
    /// Formats one price — the five FR-04-4 rows and the three forms that stand in for them
    /// (S2 4.2).
    /// </summary>
    /// <param name="price">The listing. Only <see cref="ItemPrice.PrimaryValue"/> is read.</param>
    /// <param name="rate">The divine rate, or null.</param>
    /// <param name="display">The currency <see cref="Resolve"/> chose.</param>
    /// <param name="fetchedAt">
    /// The owning category's fetch time. S4 6.1 omits this parameter, but S2 4.5.1 defines
    /// <see cref="PriceDisplay.EffectiveAsOf"/> as <c>min(category.FetchedAt, rate.AcquiredAt)</c>
    /// and no other argument carries it — <see cref="ItemPrice"/> has no timestamp.
    /// </param>
    /// <param name="now">The instant shared by the whole render pass (D-PR7).</param>
    /// <param name="rateMaxAge">From <see cref="StalenessPolicy.RateMaxAge"/>, so that Polling's
    /// inheritance rule and this gate agree by construction.</param>
    /// <param name="templates">The dictionary, consulted through the raw-template surface.</param>
    /// <remarks>
    /// Note the asymmetry: a chaos display tests two thresholds (<c>v ≥ 1</c>, then <c>d ≥ 1</c>), a
    /// divine display only one. "Chaos rows work without a rate" is true of row 3 alone — telling
    /// row 1 from row 2 needs the rate, and when it is missing the answer is
    /// <see cref="PriceForm.ChaosRatePending"/> rather than a bare <c>359.7c</c> that would claim
    /// "under one divine".
    /// </remarks>
    public static PriceDisplay Format(
        ItemPrice price,
        DivineRate? rate,
        ResolvedCurrency display,
        DateTimeOffset fetchedAt,
        DateTimeOffset now,
        TimeSpan rateMaxAge,
        ITemplateSource templates)
    {
        if (price is null)
        {
            return Unavailable(fetchedAt, templates);
        }

        // 0. The rate gate. Time is an argument; expiry is not "under one divine" (S2 4.5.4).
        // The lower bound on the rate is this file's addition: S2 4.2 gates on ">0" only, and a
        // positive-but-minute rate overflows the division below, which would throw.
        var usableRate = rate is not null
            && rate.ChaosPerDivine > 0m
            && rate.ChaosPerDivine >= NumberFormatter.MinPrice
            && (now - rate.AcquiredAt) <= rateMaxAge
                ? rate
                : null;

        // 1. The floor. Division is never reached from here (D-PR8).
        var v = price.PrimaryValue;
        if (v <= 0m || v < NumberFormatter.MinPrice)
        {
            return Unavailable(fetchedAt, templates);
        }

        // 2. d, if a rate is usable at all.
        decimal? d = null;
        if (usableRate is not null)
        {
            if (!TryDivide(v, usableRate.ChaosPerDivine, out var quotient))
            {
                return Unavailable(fetchedAt, templates);
            }

            d = quotient;
        }

        // 3. The branch.
        if (display == ResolvedCurrency.Chaos)
        {
            if (v >= 1m)
            {
                if (d is null)
                {
                    return RateIndependent(
                        PriceForm.ChaosRatePending,
                        Tmpl(templates, PriceKeys.ChaosRatePending, PriceTemplates.ChaosRatePending, NumberFormatter.Num(v)),
                        fetchedAt);
                }

                if (d.Value >= 1m)
                {
                    // Row 1. No epsilon: 194.6m / 194.6m is exactly 1m.
                    return RateDependent(
                        PriceForm.ChaosWithDivine,
                        Tmpl(
                            templates,
                            PriceKeys.ChaosWithDivine,
                            PriceTemplates.ChaosWithDivine,
                            NumberFormatter.Num(v),
                            NumberFormatter.Num(d.Value)),
                        fetchedAt,
                        usableRate);
                }

                // Row 2. The rate is invisible in the text but decided the branch, so the age and
                // the inheritance mark are inherited all the same (S2 4.5.2).
                return RateDependent(
                    PriceForm.ChaosOnly,
                    Tmpl(templates, PriceKeys.Chaos, PriceTemplates.Chaos, NumberFormatter.Num(v)),
                    fetchedAt,
                    usableRate);
            }

            // Row 3 — the only rate-independent row. v ≥ MinPrice, so 1/v cannot overflow.
            if (!TryDivide(1m, v, out var perChaos))
            {
                return Unavailable(fetchedAt, templates);
            }

            return RateIndependent(
                PriceForm.ChaosReciprocal,
                Tmpl(templates, PriceKeys.PerChaos, PriceTemplates.PerChaos, NumberFormatter.Num(perChaos)),
                fetchedAt);
        }

        if (d is null || usableRate is null)
        {
            return RateIndependent(
                PriceForm.RatePending,
                Tmpl(templates, PriceKeys.RatePending, PriceTemplates.RatePending),
                fetchedAt);
        }

        if (d.Value >= 1m)
        {
            // Row 4.
            return RateDependent(
                PriceForm.DivineOnly,
                Tmpl(templates, PriceKeys.Divine, PriceTemplates.Divine, NumberFormatter.Num(d.Value)),
                fetchedAt,
                usableRate);
        }

        // Row 5 is r / v, not 1 / d: two decimal divisions each truncate and the errors compound.
        // 194.6 / 0.06401 = 3040.1499… → 3,040, which is the response's own maxVolumeRate.
        if (!TryDivide(usableRate.ChaosPerDivine, v, out var perDivine))
        {
            return Unavailable(fetchedAt, templates);
        }

        return RateDependent(
            PriceForm.DivineReciprocal,
            Tmpl(templates, PriceKeys.PerDivine, PriceTemplates.PerDivine, NumberFormatter.Num(perDivine)),
            fetchedAt,
            usableRate);
    }

    /// <summary>
    /// Renders an age as <c>just now</c> / <c>{n}s ago</c> / … (S2 4.5.7).
    /// </summary>
    /// <remarks>
    /// Truncated, not rounded: two minutes fifty-nine seconds is <c>2m ago</c>. Under-stating the
    /// age is safe only because the staleness <em>verdict</em> is a raw <see cref="TimeSpan"/>
    /// comparison elsewhere; without that split, "10m ago with no stale mark" appears near the
    /// threshold. A clock that has run backwards is clamped to <c>just now</c>.
    /// </remarks>
    public static string Relative(DateTimeOffset at, DateTimeOffset now, ITemplateSource templates)
    {
        var delta = now - at;
        if (delta < TimeSpan.Zero || delta < JustNowWindow)
        {
            return Tmpl(templates, PriceKeys.JustNow, PriceTemplates.JustNow);
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return Tmpl(templates, PriceKeys.SecondsAgo, PriceTemplates.SecondsAgo, Whole(delta.TotalSeconds));
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return Tmpl(templates, PriceKeys.MinutesAgo, PriceTemplates.MinutesAgo, Whole(delta.TotalMinutes));
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return Tmpl(templates, PriceKeys.HoursAgo, PriceTemplates.HoursAgo, Whole(delta.TotalHours));
        }

        return Tmpl(templates, PriceKeys.DaysAgo, PriceTemplates.DaysAgo, Whole(delta.TotalDays));
    }

    /// <summary>Whole units, never grouped (S2 4.5.7).</summary>
    private static string Whole(double units)
        => ((long)Math.Floor(units)).ToString(CultureInfo.InvariantCulture);

    private static PriceDisplay Unavailable(DateTimeOffset fetchedAt, ITemplateSource templates)
        => new(
            PriceForm.Unavailable,
            Tmpl(templates, PriceKeys.Unavailable, PriceTemplates.Unavailable),
            fetchedAt,
            RateInherited: false);

    private static PriceDisplay RateIndependent(PriceForm form, string text, DateTimeOffset fetchedAt)
        => new(form, text, fetchedAt, RateInherited: false);

    private static PriceDisplay RateDependent(
        PriceForm form,
        string text,
        DateTimeOffset fetchedAt,
        DivineRate? usableRate)
    {
        if (usableRate is null)
        {
            return new PriceDisplay(form, text, fetchedAt, RateInherited: false);
        }

        var effective = usableRate.AcquiredAt < fetchedAt ? usableRate.AcquiredAt : fetchedAt;
        return new PriceDisplay(form, text, effective, usableRate.Inherited);
    }

    /// <summary>
    /// Divides without ever throwing.
    /// </summary>
    /// <remarks>
    /// <see cref="NumberFormatter.MinPrice"/> keeps the operands of the two documented divisions in
    /// range, but neither <c>PrimaryValue</c> nor <c>ChaosPerDivine</c> has an upper bound, and
    /// <c>decimal</c> division overflows near <c>7.9e28</c>. The catch is narrow and its observable
    /// result is <see cref="PriceForm.Unavailable"/>, which satisfies D15.
    /// </remarks>
    private static bool TryDivide(decimal dividend, decimal divisor, out decimal result)
    {
        try
        {
            result = dividend / divisor;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
        catch (DivideByZeroException)
        {
            result = 0m;
            return false;
        }
    }

    /// <summary>
    /// The three-net template helper (S2 4.6.2).
    /// </summary>
    /// <remarks>
    /// ① a missing key (chain level ⑤) uses the constant; ② a template that would swallow an
    /// argument uses the constant; ③ a <see cref="FormatException"/> uses the constant. Every
    /// argument is already a formatted <see cref="string"/> (D-PR4) so that <c>string.Format</c>
    /// cannot re-format a number under the calling thread's culture — the rule is enforced by the
    /// parameter type, not by convention.
    /// <para>
    /// <c>internal</c> rather than <c>private</c> so that <c>Presentation.UiStateFormat.Ui</c>
    /// (S4 11.8 G1) is the same three nets rather than a second copy of them. S4 called for an
    /// isomorphic helper; sharing this one removes the way the two could drift apart, and the
    /// sentinel cache is shared with it.
    /// </para>
    /// </remarks>
    internal static string Tmpl(
        ITemplateSource templates,
        string key,
        string fallbackConst,
        params string[] args)
    {
        var template = fallbackConst;
        if (templates is not null && templates.TryGetTemplate(key, out var fromDictionary))
        {
            template = fromDictionary;
        }

        if (!SentinelOk(template, args.Length))
        {
            template = fallbackConst;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            // Nothing is logged because Pricing has no logger; the observable result is the
            // fallback string itself (S2 9.5).
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, fallbackConst, args);
        }
        catch (FormatException)
        {
            return fallbackConst;
        }
    }

    /// <summary>
    /// True when <paramref name="template"/> consumes exactly <paramref name="count"/> arguments.
    /// </summary>
    /// <remarks>
    /// Scanning for the text <c>{0}</c> passes <c>"{{0}}c"</c>, which renders the literal
    /// <c>{0}c</c> — precisely the loss of function the constants exist to prevent. Formatting with
    /// unique sentinels catches the escape, the count mismatch and the missing slot in one pass.
    /// </remarks>
    private static bool SentinelOk(string template, int count)
        => SentinelCache.GetOrAdd((count, template), static state => Probe(state.Template, state.Count));

    private static bool Probe(string template, int count)
    {
        var sentinels = new object[count];
        for (var i = 0; i < count; i++)
        {
            sentinels[i] = Sentinel(i);
        }

        string formatted;
        try
        {
            formatted = string.Format(CultureInfo.InvariantCulture, template, sentinels);
        }
        catch (FormatException)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!formatted.Contains(Sentinel(i), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // Control characters, so a sentinel can never collide with a template's own text.
    private static string Sentinel(int index)
        => string.Concat("\u0001", index.ToString(CultureInfo.InvariantCulture), "\u0002");
}
