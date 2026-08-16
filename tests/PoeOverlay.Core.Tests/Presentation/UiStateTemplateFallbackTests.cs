using System.Reflection;
using System.Text.Json;
using PoeOverlay.Core.Presentation.UiState;
using Xunit;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// S3 9.3 D-PS8 / S4 16.2 — the <c>ui.state.*</c>, <c>ui.tray.*</c> and <c>ui.overlay.*</c>
/// constants, the embedded dictionary and the S4 14 catalogue are one thing.
/// </summary>
/// <remarks>
/// The C1 test for Pricing, applied to the second constant table. It matters more here than it
/// looks: the fifth level of the fallback chain renders the key itself, which is survivable for a
/// label and is not survivable for <c>rate pending for {0}</c> — the number simply disappears.
/// </remarks>
public sealed class UiStateTemplateFallbackTests
{
    /// <summary>S4 14.3, 14.6, 18.3 and the five must-see rows of 14.4, transcribed.</summary>
    private static readonly Dictionary<string, (string Constant, string Value)> Catalogue = new(StringComparer.Ordinal)
    {
        ["ui.state.ratePendingDuration"] = (nameof(UiStateTemplates.RatePendingWithDuration), "rate pending for {0}"),
        ["ui.state.pollingStoppedStale"] = (nameof(UiStateTemplates.PollingStoppedStale), "updates are delayed. last attempt {0}"),
        ["ui.state.pollingStoppedExited"] = (nameof(UiStateTemplates.PollingStoppedExited), "updates have stopped. restart the app"),
        ["ui.state.commitRejected"] = (nameof(UiStateTemplates.CommitRejectedBanner), "prices are not updating. check the league setting"),
        ["ui.state.rateInherited"] = (nameof(UiStateTemplates.RateInheritedFooter), "rate carried over"),
        ["ui.state.itemDropped"] = (nameof(UiStateTemplates.ItemDroppedRow), "price unavailable — item still exists"),
        ["ui.state.itemUnresolved"] = (nameof(UiStateTemplates.ItemUnresolvedRow), "item not found"),
        ["ui.state.fetchFailedRow"] = (nameof(UiStateTemplates.FetchFailedRow), "update failed {0}"),
        ["ui.state.fetchFailedBadge"] = (nameof(UiStateTemplates.FetchFailedBadge), "{0} categories failed to update"),
        ["ui.state.loggingUnavailable"] = (nameof(UiStateTemplates.LoggingUnavailableWithPath), "log file unavailable — path: {0}"),
        ["ui.tray.tooltipMore"] = (nameof(UiStateTemplates.TrayTooltipMore), "(+{0} more)"),
        ["ui.overlay.moreRows"] = (nameof(UiStateTemplates.MoreRows), "+{0} more"),
        ["ui.overlay.moreRowsExplicit"] = (nameof(UiStateTemplates.MoreRowsExplicit), "+{0} more — adjust height in settings"),
    };

    /// <summary>The placeholder-free keys S4 14.4 deliberately leaves without a constant.</summary>
    private static readonly string[] KeysWithoutConstants =
    [
        "ui.state.leagueUnresolved",
        "ui.state.settingsWriteFailed",
        "ui.state.settingsCorrupt",
        "ui.state.settingsReadOnly",
        "ui.state.settingsUnreadable",
        "ui.state.trayUnavailable",
        "ui.state.viewModelRefreshFailing",
    ];

    [Fact]
    public void Constants_AndTheEmbeddedDictionary_AgreeCharacterForCharacter()
    {
        var embedded = ReadEmbedded();
        var constants = Constants();

        foreach (var (key, (constantName, expected)) in Catalogue)
        {
            Assert.True(embedded.ContainsKey(key), $"the embedded dictionary is missing {key}");
            Assert.True(constants.ContainsKey(constantName), $"UiStateTemplates is missing {constantName}");

            Assert.Equal(expected, embedded[key]);
            Assert.Equal(expected, constants[constantName]);
        }
    }

    [Fact]
    public void EveryConstant_HasACatalogueRow()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, (constantName, _)) in Catalogue)
        {
            expected.Add(constantName);
        }

        // A new constant with no catalogue row has no dictionary key either, so it would render as
        // the raw key in a language nobody tests.
        Assert.Equal(
            expected.OrderBy(n => n, StringComparer.Ordinal),
            Constants().Keys.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryKeyLiteral_ResolvesInTheEmbeddedDictionary()
    {
        var embedded = ReadEmbedded();

        foreach (var key in KeyLiterals())
        {
            Assert.True(embedded.ContainsKey(key), $"UiStateKeys names {key}, which the dictionary lacks");
        }
    }

    [Fact]
    public void ThePlaceholderFreeKeys_DeliberatelyHaveNoConstant()
    {
        var constantValues = new HashSet<string>(Constants().Values, StringComparer.Ordinal);
        var embedded = ReadEmbedded();

        foreach (var key in KeysWithoutConstants)
        {
            Assert.True(embedded.ContainsKey(key));

            // S4 14's corrected rule: the fifth chain level is a good enough answer for these, so
            // adding a constant here means the rule and the table have drifted apart again.
            Assert.DoesNotContain(embedded[key], constantValues);
        }
    }

    [Fact]
    public void EveryPlaceholderBearingConstant_DeclaresTheArgumentCountItsCallersPass()
    {
        foreach (var (_, (constantName, value)) in Catalogue)
        {
            var expectsArgument = value.Contains("{0}", StringComparison.Ordinal);
            var isSingleArgument = constantName is not nameof(UiStateTemplates.PollingStoppedExited)
                and not nameof(UiStateTemplates.CommitRejectedBanner)
                and not nameof(UiStateTemplates.RateInheritedFooter)
                and not nameof(UiStateTemplates.ItemDroppedRow)
                and not nameof(UiStateTemplates.ItemUnresolvedRow);

            Assert.Equal(isSingleArgument, expectsArgument);
            Assert.DoesNotContain("{1}", value, StringComparison.Ordinal);
        }
    }

    private static Dictionary<string, string> Constants()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in typeof(UiStateTemplates).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            {
                result[field.Name] = (string)field.GetRawConstantValue()!;
            }
        }

        return result;
    }

    private static IEnumerable<string> KeyLiterals()
    {
        foreach (var field in typeof(UiStateKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            {
                yield return (string)field.GetRawConstantValue()!;
            }
        }
    }

    private static Dictionary<string, string> ReadEmbedded()
    {
        var assembly = typeof(UiStateTemplates).Assembly;
        using var stream = assembly.GetManifestResourceStream("PoeOverlay.Core.Localization.en.json")
            ?? throw new InvalidOperationException("the embedded dictionary is missing");

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("the embedded dictionary did not parse");

        return new Dictionary<string, string>(parsed, StringComparer.Ordinal);
    }
}
