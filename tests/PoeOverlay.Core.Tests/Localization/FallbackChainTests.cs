using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using Xunit;

namespace PoeOverlay.Core.Tests.Localization;

/// <summary>
/// S2 11.6 (L1–L10) — the five fallback levels, the blank-is-not-a-hit rule, the language in the
/// suppression key, and the load-time placeholder check (S2 3.7, D-L3).
/// </summary>
/// <remarks>
/// Assertions are on the level that answered, not only on the string, so a dictionary edit cannot
/// make a test pass for the wrong reason.
/// </remarks>
public sealed class FallbackChainTests
{
    private const string Key = "ui.state.leagueUnresolved";

    [Fact]
    public void L1_KeyPresentInCurrentLanguage_AnswersFromLevelOne()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { [Key] = "리그를 확인할 수 없습니다" });
        harness.WriteDictionary("en", new Dictionary<string, string> { [Key] = "disk english" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        var value = catalog.Resolve(Key, apiName: null, isItemName: false, out var level);

        Assert.Equal(1, level);
        Assert.Equal("리그를 확인할 수 없습니다", value);
    }

    [Fact]
    public void L2_KeyMissingFromCurrentLanguage_FallsToTheDiskEnglish()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { ["ui.tray.exit"] = "종료" });
        harness.WriteDictionary("en", new Dictionary<string, string> { [Key] = "disk english" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        var value = catalog.Resolve(Key, apiName: null, isItemName: false, out var level);

        Assert.Equal(2, level);
        Assert.Equal("disk english", value);
    }

    [Fact]
    public void L3_NeitherCurrentNorDiskEnglish_FallsToTheEmbeddedFloor()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { ["ui.tray.exit"] = "종료" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        var value = catalog.Resolve(Key, apiName: null, isItemName: false, out var level);

        Assert.Equal(3, level);
        Assert.Equal("could not determine the league", value);
    }

    [Fact]
    public void L4_UntranslatedItemName_UsesTheApiNameAndLogsDebugOnce()
    {
        using var harness = LocalizationHarness.Create();
        var catalog = harness.Start();

        var first = catalog.ItemName(new ItemId("exalted-orb"), "Exalted Orb");
        var second = catalog.ItemName(new ItemId("exalted-orb"), "Exalted Orb");

        Assert.Equal("Exalted Orb", first);
        Assert.Equal("Exalted Orb", second);
        Assert.Equal(1, harness.Logger.Count(LogLevel.Debug, "exalted-orb"));
    }

    [Fact]
    public void L5_NoApiNameEither_RendersTheSlugAndWarnsOnce()
    {
        using var harness = LocalizationHarness.Create();
        var catalog = harness.Start();

        var first = catalog.ItemName(new ItemId("exalted-orb"), apiName: null);
        var second = catalog.ItemName(new ItemId("exalted-orb"), apiName: "   ");

        Assert.Equal("exalted-orb", first);
        Assert.Equal("exalted-orb", second);
        Assert.Equal(1, harness.Logger.Count(LogLevel.Warning, "exalted-orb"));
    }

    [Fact]
    public void L6_BlankValueIsNotAHit_SoLevelOneIsSkipped()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { [Key] = "  " });
        harness.WriteDictionary("en", new Dictionary<string, string> { [Key] = "disk english" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        var value = catalog.Resolve(Key, apiName: null, isItemName: false, out var level);

        Assert.Equal(2, level);
        Assert.Equal("disk english", value);
    }

    [Fact]
    public void L7_CurrentLanguageIsEnglish_CollapsesLevelsOneAndTwoIntoOneLookup()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("en", new Dictionary<string, string> { [Key] = "disk english" });

        var catalog = harness.Start();

        Assert.Equal("en", catalog.CurrentLanguage);

        var value = catalog.Resolve(Key, apiName: null, isItemName: false, out var level);

        // Level 1 is never entered for en — the same table would otherwise be consulted twice.
        Assert.Equal(2, level);
        Assert.Equal("disk english", value);
    }

    [Fact]
    public void L8_SuppressionKeyCarriesTheLanguage_SoASwitchReportsTheSameKeyAgain()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { ["ui.tray.exit"] = "종료" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        catalog.Resolve("ui.does.not.exist", apiName: null, isItemName: false, out _);
        catalog.Resolve("ui.does.not.exist", apiName: null, isItemName: false, out _);
        Assert.Equal(1, harness.Logger.Count(LogLevel.Warning, "ui.does.not.exist"));

        catalog.SetLanguage("en");
        catalog.Resolve("ui.does.not.exist", apiName: null, isItemName: false, out var level);

        Assert.Equal(5, level);
        Assert.Equal(2, harness.Logger.Count(LogLevel.Warning, "ui.does.not.exist"));
    }

    [Fact]
    public void L9_TranslationWithAWrongPlaceholderIndex_IsDroppedAtLoadWithOneWarning()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary(
            "ko",
            new Dictionary<string, string> { ["ui.price.chaosWithDivine"] = "{0}c ({2}d)" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        var value = catalog.Resolve("ui.price.chaosWithDivine", apiName: null, isItemName: false, out var level);

        // Dropped, so the chain descends — and, unlike Pricing's per-render nets, the cause is
        // in the log.
        Assert.Equal(3, level);
        Assert.Equal("{0}c ({1}d)", value);
        Assert.Equal(1, harness.Logger.Count(LogLevel.Warning, "ui.price.chaosWithDivine"));
    }

    [Fact]
    public void L9_TranslationWithTheRightPlaceholders_SurvivesTheLoadCheck()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary(
            "ko",
            new Dictionary<string, string> { ["ui.price.perDivine"] = "1d당 {0}개" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        var value = catalog.Resolve("ui.price.perDivine", apiName: null, isItemName: false, out var level);

        Assert.Equal(1, level);
        Assert.Equal("1d당 {0}개", value);
    }

    [Fact]
    public void L9_EscapedPlaceholderInATranslation_IsAlsoDropped()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { ["ui.price.chaos"] = "{{0}}c" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        var value = catalog.Resolve("ui.price.chaos", apiName: null, isItemName: false, out var level);

        Assert.Equal(3, level);
        Assert.Equal("{0}c", value);
    }

    [Fact]
    public void L10_ScriptSubtagFile_IsDiscoveredByTheWidenedPattern()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary(
            "zh-Hans",
            new Dictionary<string, string> { ["ui.language.selfName"] = "简体中文" });

        var catalog = harness.Start();

        Assert.Contains(catalog.Languages, l => l.Tag == "zh-Hans" && l.DisplayName == "简体中文");

        catalog.SetLanguage("zh-Hans");
        Assert.Equal("zh-Hans", catalog.CurrentLanguage);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("ko", true)]
    [InlineData("fil", true)]
    [InlineData("zh-Hans", true)]
    [InlineData("sr-Latn-RS", true)]
    [InlineData("pt-BR", true)]
    [InlineData("EN", false)]
    [InlineData("en-us", false)]
    [InlineData("e", false)]
    [InlineData("english", false)]
    [InlineData("en_US", false)]
    public void LanguageTagValidator_AcceptsExactlyTheDocumentedShapes(string stem, bool expected)
        => Assert.Equal(expected, LanguageTagValidator.IsValid(stem));

    [Fact]
    public void InvalidFileStem_IsIgnoredWithAWarningAndCostsNoOtherLanguage()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("english", new Dictionary<string, string> { [Key] = "ignored" });
        harness.WriteDictionary("ko", new Dictionary<string, string> { [Key] = "kept" });

        var catalog = harness.Start();

        Assert.DoesNotContain(catalog.Languages, l => l.Tag == "english");
        Assert.Contains(catalog.Languages, l => l.Tag == "ko");
        Assert.Equal(1, harness.Logger.Count(LogLevel.Warning, "english"));
    }

    [Fact]
    public void UnparseableFile_CostsOnlyThatLanguage()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteRaw("ko", "{ this is not json");
        harness.WriteDictionary("de", new Dictionary<string, string> { [Key] = "kept" });

        var catalog = harness.Start();

        Assert.DoesNotContain(catalog.Languages, l => l.Tag == "ko");
        Assert.Contains(catalog.Languages, l => l.Tag == "de");

        catalog.SetLanguage("de");
        Assert.Equal("kept", catalog.Resolve(Key, apiName: null, isItemName: false, out _));
    }

    [Fact]
    public void SetLanguage_UnknownTag_FallsBackToEnglishWithAWarning()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { [Key] = "kept" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");
        catalog.SetLanguage("xx");

        Assert.Equal("en", catalog.CurrentLanguage);
        Assert.Equal(1, harness.Logger.Count(LogLevel.Warning, "xx"));
    }

    [Fact]
    public void SetLanguage_PublishesBeforeItAnnounces()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { [Key] = "kept" });

        var catalog = harness.Start();
        string? observed = null;
        catalog.LanguageChanged += (_, _) => observed = catalog.CurrentLanguage;

        catalog.SetLanguage("ko");

        Assert.Equal("ko", observed);
    }

    [Fact]
    public void Languages_AlwaysContainsTheEmbeddedFloor()
    {
        using var harness = LocalizationHarness.Create();
        var catalog = harness.Start();

        Assert.Contains(catalog.Languages, l => l.Tag == "en" && l.DisplayName == "English");
    }

    [Fact]
    public void TryGetTemplate_UnresolvedKey_IsFalseWithANonNullOut()
    {
        using var harness = LocalizationHarness.Create();
        var catalog = harness.Start();

        Assert.False(catalog.TryGetTemplate("ui.does.not.exist", out var template));
        Assert.NotNull(template);
        Assert.True(catalog.TryGetTemplate("ui.price.chaos", out var chaos));
        Assert.Equal("{0}c", chaos);
    }

    [Fact]
    public void Ui_FormatsWithTheInvariantCultureAndNeverThrowsOnABadTemplate()
    {
        using var harness = LocalizationHarness.Create();
        harness.WriteDictionary("ko", new Dictionary<string, string> { ["ui.not.catalogued"] = "{0} and {1}" });

        var catalog = harness.Start();
        catalog.SetLanguage("ko");

        Assert.Equal("a and b", catalog.Ui("ui.not.catalogued", "a", "b"));

        // One argument short: the template comes back unformatted rather than throwing.
        Assert.Equal("{0} and {1}", catalog.Ui("ui.not.catalogued", "a"));
    }
}
