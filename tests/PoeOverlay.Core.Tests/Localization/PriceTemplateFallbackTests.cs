using System.Globalization;
using System.Reflection;
using System.Text.Json;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Pricing;
using Xunit;

namespace PoeOverlay.Core.Tests.Localization;

/// <summary>
/// S2 11.11 (C1–C7) — the compile-time constants, the embedded dictionary and the S4 14 catalogue
/// must be one thing, and each of Pricing's three nets must actually catch what it claims to.
/// </summary>
/// <remarks>
/// C1 is the reason the constants can be trusted at all: the fallback constant is only a fallback
/// if it is the same string the dictionary would have produced.
/// </remarks>
public sealed class PriceTemplateFallbackTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    /// <summary>S4 14.1 and 14.2, transcribed. Key → (constant name, English value).</summary>
    private static readonly Dictionary<string, (string Constant, string Value)> Catalogue = new(StringComparer.Ordinal)
    {
        ["ui.price.chaos"] = (nameof(PriceTemplates.Chaos), "{0}c"),
        ["ui.price.divine"] = (nameof(PriceTemplates.Divine), "{0}d"),
        ["ui.price.chaosWithDivine"] = (nameof(PriceTemplates.ChaosWithDivine), "{0}c ({1}d)"),
        ["ui.price.chaosRatePending"] = (nameof(PriceTemplates.ChaosRatePending), "{0}c (rate pending)"),
        ["ui.price.perChaos"] = (nameof(PriceTemplates.PerChaos), "{0} per 1c"),
        ["ui.price.perDivine"] = (nameof(PriceTemplates.PerDivine), "{0} per 1d"),
        ["ui.price.ratePending"] = (nameof(PriceTemplates.RatePending), "rate pending"),
        ["ui.price.unavailable"] = (nameof(PriceTemplates.Unavailable), "—"),
        ["ui.time.justNow"] = (nameof(PriceTemplates.JustNow), "just now"),
        ["ui.time.secondsAgo"] = (nameof(PriceTemplates.SecondsAgo), "{0}s ago"),
        ["ui.time.minutesAgo"] = (nameof(PriceTemplates.MinutesAgo), "{0}m ago"),
        ["ui.time.hoursAgo"] = (nameof(PriceTemplates.HoursAgo), "{0}h ago"),
        ["ui.time.daysAgo"] = (nameof(PriceTemplates.DaysAgo), "{0}d ago"),
    };

    [Fact]
    public void C1_EmbeddedDictionaryAndConstants_AgreeCharacterForCharacter()
    {
        var embedded = ReadEmbedded();
        var constants = Constants();

        foreach (var (key, (constantName, expected)) in Catalogue)
        {
            Assert.True(embedded.ContainsKey(key), $"the embedded dictionary is missing {key}");
            Assert.True(constants.ContainsKey(constantName), $"PriceTemplates is missing {constantName}");

            Assert.Equal(expected, embedded[key]);
            Assert.Equal(expected, constants[constantName]);
        }
    }

    [Fact]
    public void C1_EveryPriceTemplateConstant_HasACatalogueRow()
    {
        var expectedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, (constantName, _)) in Catalogue)
        {
            expectedNames.Add(constantName);
        }

        // A new constant with no catalogue row (and so no dictionary key) fails here rather than
        // silently rendering the raw key in a language nobody tests.
        Assert.Equal(expectedNames.OrderBy(n => n, StringComparer.Ordinal), Constants().Keys.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void C1_EveryUiKeyInTheEmbeddedDictionary_IsCatalogued()
    {
        var embedded = ReadEmbedded();

        foreach (var key in embedded.Keys)
        {
            Assert.True(
                UiKeyCatalog.TryGetArgumentCount(key, out _),
                $"{key} is in en.json but not in the S4 14 catalogue");
        }

        foreach (var key in UiKeyCatalog.Keys)
        {
            Assert.True(embedded.ContainsKey(key), $"{key} is catalogued but missing from en.json");
        }
    }

    [Fact]
    public void C1_EveryEmbeddedTemplate_UsesExactlyTheCataloguedArgumentCount()
    {
        var embedded = ReadEmbedded();

        foreach (var (key, value) in embedded)
        {
            Assert.True(UiKeyCatalog.TryGetArgumentCount(key, out var expected));
            Assert.True(
                ConsumesExactly(value, expected),
                $"{key} = \"{value}\" does not use exactly {expected} placeholder(s)");
        }
    }

    [Fact]
    public void C1_EveryPricingCallSite_PassesTheCataloguedArgumentCount()
    {
        // The probe hands back a template with exactly the catalogued number of slots, prefixed by
        // a marker. If a call site passed a different number, the sentinel net would reject the
        // probe template and Pricing would fall back to its constant — so the marker would be gone.
        var probe = new ArgumentCountProbe();

        foreach (var display in Exercise(probe))
        {
            Assert.StartsWith(ArgumentCountProbe.Marker, display, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void C2_MissingKey_StillPrintsTheNumber()
    {
        var templates = MutableTemplateSource.FromEmbedded();
        templates.Remove("ui.price.chaos");

        Assert.Equal("43.5c", Chaos(43.5m, templates));
    }

    [Fact]
    public void C3_TemplateWithoutAPlaceholder_FallsBackToTheConstant()
    {
        var templates = MutableTemplateSource.FromEmbedded();
        templates.Set("ui.price.chaos", "가격");

        Assert.Equal("43.5c", Chaos(43.5m, templates));
    }

    [Fact]
    public void C4_EscapedPlaceholder_FallsBackToTheConstant()
    {
        var templates = MutableTemplateSource.FromEmbedded();
        templates.Set("ui.price.chaos", "{{0}}c");

        // The first edition's "does it contain {0}" scan passed this and rendered the literal
        // "{0}c" — the loss of function the constants exist to prevent.
        Assert.Equal("43.5c", Chaos(43.5m, templates));
    }

    [Fact]
    public void C5_TemplateWithTooManySlots_FallsBackWithoutLeakingAFormatException()
    {
        // The two-argument template used to be ui.price.change; it went with FR-04-1, and
        // chaosWithDivine is now the only two-slot price template left to exercise the arity net.
        var templates = MutableTemplateSource.FromEmbedded();
        templates.Set("ui.price.chaosWithDivine", "{0}c ({1}d) {2}");

        Assert.Equal("359.7c (1.85d)", Chaos(359.7m, templates));
    }

    [Fact]
    public void C6_TemplateWithTooFewSlots_FallsBackToTheConstant()
    {
        var templates = MutableTemplateSource.FromEmbedded();
        templates.Set("ui.price.chaosWithDivine", "{0}c");

        Assert.Equal("359.7c (1.85d)", Chaos(359.7m, templates));
    }

    [Fact]
    public void C7_EveryPriceForm_HasNonNullText()
    {
        var templates = MutableTemplateSource.FromEmbedded();
        var seen = new HashSet<PriceForm>();

        foreach (var display in AllForms(templates))
        {
            Assert.NotNull(display.Text);
            Assert.NotEqual(string.Empty, display.Text);
            seen.Add(display.Form);
        }

        Assert.Equal(Enum.GetValues<PriceForm>().Length, seen.Count);
    }

    [Fact]
    public void C7_EveryPriceForm_SurvivesAnEntirelyEmptyDictionary()
    {
        var empty = new MutableTemplateSource();

        foreach (var display in AllForms(empty))
        {
            Assert.NotNull(display.Text);
            Assert.NotEqual(string.Empty, display.Text);
        }

        Assert.Equal("43.5c", Chaos(43.5m, empty));
        Assert.Equal("—", PricingEngine.Format(Price(0m), Rate(), ResolvedCurrency.Chaos, Now, Now, MaxAge, empty).Text);
    }

    private static IEnumerable<PriceDisplay> AllForms(ITemplateSource templates)
    {
        var rate = Rate();
        yield return PricingEngine.Format(Price(359.7m), rate, ResolvedCurrency.Chaos, Now, Now, MaxAge, templates);
        yield return PricingEngine.Format(Price(43.5m), rate, ResolvedCurrency.Chaos, Now, Now, MaxAge, templates);
        yield return PricingEngine.Format(Price(0.0644m), rate, ResolvedCurrency.Chaos, Now, Now, MaxAge, templates);
        yield return PricingEngine.Format(Price(359.7m), rate, ResolvedCurrency.Divine, Now, Now, MaxAge, templates);
        yield return PricingEngine.Format(Price(0.06401m), rate, ResolvedCurrency.Divine, Now, Now, MaxAge, templates);
        yield return PricingEngine.Format(Price(359.7m), rate: null, ResolvedCurrency.Chaos, Now, Now, MaxAge, templates);
        yield return PricingEngine.Format(Price(359.7m), rate: null, ResolvedCurrency.Divine, Now, Now, MaxAge, templates);
        yield return PricingEngine.Format(Price(0m), rate, ResolvedCurrency.Chaos, Now, Now, MaxAge, templates);
    }

    private static IEnumerable<string> Exercise(ITemplateSource templates)
    {
        foreach (var display in AllForms(templates))
        {
            yield return display.Text;
        }

        yield return PricingEngine.Relative(Now, Now, templates);
        yield return PricingEngine.Relative(Now - TimeSpan.FromSeconds(30), Now, templates);
        yield return PricingEngine.Relative(Now - TimeSpan.FromMinutes(30), Now, templates);
        yield return PricingEngine.Relative(Now - TimeSpan.FromHours(5), Now, templates);
        yield return PricingEngine.Relative(Now - TimeSpan.FromDays(5), Now, templates);
    }

    private static string Chaos(decimal value, ITemplateSource templates)
        => PricingEngine.Format(Price(value), Rate(), ResolvedCurrency.Chaos, Now, Now, MaxAge, templates).Text;

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

    private static DivineRate Rate() => new(194.6m, Now, "Standard", Inherited: false);

    private static Dictionary<string, string> ReadEmbedded()
    {
        using var stream = typeof(LocalizationCatalog).Assembly
            .GetManifestResourceStream(LocalizationCatalog.EmbeddedResourceName);

        Assert.NotNull(stream);
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        Assert.NotNull(entries);
        return entries;
    }

    private static Dictionary<string, string> Constants()
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in typeof(PriceTemplates).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field is { IsLiteral: true, IsInitOnly: false }
                && field.GetRawConstantValue() is string value)
            {
                constants[field.Name] = value;
            }
        }

        return constants;
    }

    private static bool ConsumesExactly(string template, int count)
    {
        var sentinels = new object[count];
        for (var i = 0; i < count; i++)
        {
            sentinels[i] = "\u0001" + i.ToString(CultureInfo.InvariantCulture) + "\u0002";
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
            if (!formatted.Contains((string)sentinels[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Hands back a marked template with exactly the catalogued number of slots.</summary>
    private sealed class ArgumentCountProbe : ITemplateSource
    {
        public const string Marker = "<probe>";

        public bool TryGetTemplate(string key, out string template)
        {
            if (!UiKeyCatalog.TryGetArgumentCount(key, out var count))
            {
                template = string.Empty;
                return false;
            }

            var slots = new string[count];
            for (var i = 0; i < count; i++)
            {
                slots[i] = "{" + i.ToString(CultureInfo.InvariantCulture) + "}";
            }

            template = Marker + string.Concat(slots);
            return true;
        }
    }

    /// <summary>An editable dictionary, so a test can break exactly one template.</summary>
    private sealed class MutableTemplateSource : ITemplateSource
    {
        private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

        public static MutableTemplateSource FromEmbedded()
        {
            var source = new MutableTemplateSource();
            foreach (var (key, value) in ReadEmbedded())
            {
                source._entries[key] = value;
            }

            return source;
        }

        public void Set(string key, string value) => _entries[key] = value;

        public void Remove(string key) => _entries.Remove(key);

        public bool TryGetTemplate(string key, out string template)
        {
            if (_entries.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                template = value;
                return true;
            }

            template = string.Empty;
            return false;
        }
    }
}
