using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Settings;
using Xunit;

namespace PoeOverlay.Core.Tests.Settings;

/// <summary>
/// S2 11.10 SE1, SE2, SE4 – SE10 (S4 16.6) — reading and validating <c>settings.json</c>.
/// </summary>
public sealed class LoadValidationTests
{
    private const string Valid = """
        {
          "schemaVersion": 1,
          "league": "Allflame",
          "refreshIntervalMinutes": 10,
          "language": "ko",
          "defaultDisplayCurrency": "divine",
          "window": { "x": 12, "y": 34, "width": 500, "height": 600, "heightMode": "explicit", "opacity": 0.5 },
          "watchlist": [ { "id": "divine", "category": "Currency", "displayCurrency": "chaos" } ],
          "firstRunAcknowledged": true
        }
        """;

    [Fact]
    public async Task AValidFile_LoadsEveryKey()
    {
        using var harness = await SettingsHarness.StartedAsync(Valid);
        var settings = harness.Store.Current;

        Assert.Equal("Allflame", settings.League);
        Assert.Equal(10, settings.RefreshIntervalMinutes);
        Assert.Equal("ko", settings.Language);
        Assert.Equal(DisplayCurrency.Divine, settings.DefaultDisplayCurrency);
        Assert.Equal(new WindowSettings(12, 34, 500, 600, HeightMode.Explicit, 0.5), settings.Window);
        Assert.True(settings.FirstRunAcknowledged);
        Assert.Equal(WriteBlockReason.None, harness.Store.BlockReason);

        var entry = Assert.Single(settings.Watchlist);
        Assert.Equal(new ItemId("divine"), entry.Id);
        Assert.Equal(new CategoryRef("Currency", ExchangeCategory.Currency), entry.Category);
        Assert.Equal(DisplayCurrency.Chaos, entry.DisplayCurrency);
    }

    [Fact]
    public async Task NoFileAtAll_IsTheOrdinaryFirstRunPath()
    {
        using var harness = await SettingsHarness.StartedAsync();

        Assert.Equal(AppSettings.Default, harness.Store.Current);
        Assert.Equal(WriteBlockReason.None, harness.Store.BlockReason);
        Assert.Equal(
            SettingsStore.NoFileReason,
            Assert.IsType<SettingsLoadResult.Defaulted>(harness.Store.LastLoadResult).ReasonCode);
    }

    [Fact]
    public async Task SE1_ATruncatedDocument_IsQuarantinedAndBlocksWrites()
    {
        using var harness = await SettingsHarness.StartedAsync("{ \"schemaVersion\": 1, \"league\": \"All");

        Assert.Equal(AppSettings.Default, harness.Store.Current);
        Assert.Equal(WriteBlockReason.Corrupt, harness.Store.BlockReason);
        Assert.False(File.Exists(harness.FilePath));

        var quarantined = Assert.Single(harness.QuarantineFiles());
        Assert.Equal("settings.corrupt-20260816T070000Z.json", quarantined);

        Assert.True(harness.Sink.StateOf(AppConditionKind.SettingsCorrupt));
        Assert.Equal("ui.error.settingsCorrupt", Assert.Single(harness.Sink.Errors).MessageKey);
    }

    [Fact]
    public async Task ARootThatIsNotAnObject_IsAlsoCorrupt()
    {
        using var harness = await SettingsHarness.StartedAsync("[1, 2, 3]");

        // Valid JSON, but no key can be read from it, so it is as unusable as a truncated file.
        Assert.Equal(WriteBlockReason.Corrupt, harness.Store.BlockReason);
        Assert.Single(harness.QuarantineFiles());
    }

    [Fact]
    public async Task SE2_TwoCorruptionsInTheSameSecond_ProduceTwoFilesWithoutColliding()
    {
        using var first = await SettingsHarness.StartedAsync("not json");
        var moved = Path.Combine(first.Directory, Assert.Single(first.QuarantineFiles()));

        File.WriteAllText(first.FilePath, "still not json");
        var second = SettingsStore.Load(first.FilePath, first.Time);

        var files = first.QuarantineFiles();
        Assert.Equal(2, files.Count);
        Assert.Equal("settings.corrupt-20260816T070000Z-2.json", Path.GetFileName(
            Assert.IsType<SettingsLoadResult.Corrupt>(second).QuarantinePath));
        Assert.True(File.Exists(moved));

        // Sorting by the key equals chronological order, which is what the pruning rule depends on.
        // A raw ordinal sort of the names does not: '-' sorts before '.', so the collision file
        // comes out first — as `files` shows — and pruning would delete the newer of the two.
        Assert.Equal("settings.corrupt-20260816T070000Z-2.json", files[0]);
        Assert.Equal(
            new[] { "settings.corrupt-20260816T070000Z.json", "settings.corrupt-20260816T070000Z-2.json" },
            files.OrderBy(SettingsStore.QuarantineSortKey, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void QuarantineSortKey_OrdersByTimeAcrossSecondsAndOrdinals()
    {
        var keys = new[]
        {
            "settings.corrupt-20260816T070000Z.json",
            "settings.corrupt-20260816T070000Z-2.json",
            "settings.corrupt-20260816T070000Z-10.json",
            "settings.corrupt-20260816T070001Z.json",
        }.Select(SettingsStore.QuarantineSortKey).ToArray();

        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), keys);
    }

    [Fact]
    public async Task SE4_OneUnreadableValue_CostsOnlyThatKey()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "refreshIntervalMinutes": "five",
              "watchlist": [ { "id": "divine", "category": "Currency" } ]
            }
            """;

        using var harness = await SettingsHarness.StartedAsync(json);
        var settings = harness.Store.Current;

        // The whole reason this is a JsonDocument walk and not a deserialiser call: one bad value
        // must not cost the user their watchlist.
        Assert.Equal(AppSettings.Default.RefreshIntervalMinutes, settings.RefreshIntervalMinutes);
        Assert.Single(settings.Watchlist);
        Assert.Contains(
            "refreshIntervalMinutes",
            Assert.IsType<SettingsLoadResult.Loaded>(harness.Store.LastLoadResult).Corrections);
    }

    [Fact]
    public async Task SE5_AnUnknownCategory_IsPreservedVerbatim()
    {
        const string json = """
            { "schemaVersion": 1, "watchlist": [ { "id": "chisel", "category": "Chisel" } ] }
            """;

        using var harness = await SettingsHarness.StartedAsync(json);

        // Collapsing it would lose the user's typing on the next save, and the row can still say
        // "this category no longer exists" only because the token survived.
        var entry = Assert.Single(harness.Store.Current.Watchlist);
        Assert.Equal(new CategoryRef("Chisel", null), entry.Category);
        Assert.True(entry.Category.IsUnresolved);
    }

    [Fact]
    public async Task ANumericCategoryToken_DoesNotBecomeARealCategory()
    {
        const string json = """
            { "schemaVersion": 1, "watchlist": [ { "id": "x", "category": "1" } ] }
            """;

        using var harness = await SettingsHarness.StartedAsync(json);

        // Enum.TryParse accepts "1" and returns Currency, which would break CategoryRef's own
        // invariant that Known.ToString() equals Raw.
        var entry = Assert.Single(harness.Store.Current.Watchlist);
        Assert.Equal(new CategoryRef("1", null), entry.Category);
    }

    [Fact]
    public async Task SE6_ABlankId_IsTheOnlyReasonAnEntryIsDiscarded()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "watchlist": [
                { "id": "  ", "category": "Currency" },
                { "id": "divine", "category": "NotACategory", "displayCurrency": "sextant" }
              ]
            }
            """;

        using var harness = await SettingsHarness.StartedAsync(json);

        var entry = Assert.Single(harness.Store.Current.Watchlist);
        Assert.Equal(new ItemId("divine"), entry.Id);

        // An unknown display currency becomes "omitted", not Auto: forcing Auto would destroy the
        // difference between an explicit Auto and an inherited default.
        Assert.Null(entry.DisplayCurrency);
        Assert.Contains(
            "watchlist[0].id",
            Assert.IsType<SettingsLoadResult.Loaded>(harness.Store.LastLoadResult).Corrections);
    }

    [Theory]
    [InlineData("\"exalted\"")]
    [InlineData("7")]
    [InlineData("null")]
    public async Task AnUnusableTopLevelDisplayCurrency_BecomesAutoAndIsRecorded(string value)
    {
        var json = $$"""{ "schemaVersion": 1, "defaultDisplayCurrency": {{value}} }""";

        using var harness = await SettingsHarness.StartedAsync(json);

        // Auto, not "omitted" as the per-entry key becomes: the top level is the value every entry
        // that omits its own inherits, so there is nothing further up for it to fall back to.
        Assert.Equal(DisplayCurrency.Auto, harness.Store.Current.DefaultDisplayCurrency);

        // The note is the whole difference between this and a file that simply has no such key. An
        // unknown token, a wrong type and an explicit null all arrive here as the same null string,
        // and without the note the user's typo would be corrected in silence and then written back
        // over on the next save.
        Assert.Contains(
            "defaultDisplayCurrency",
            Assert.IsType<SettingsLoadResult.Loaded>(harness.Store.LastLoadResult).Corrections);
    }

    [Fact]
    public async Task SE7_DuplicateIds_KeepTheFirstAndPreserveOrder()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "watchlist": [
                { "id": "divine", "category": "Currency" },
                { "id": "chaos",  "category": "Currency" },
                { "id": "divine", "category": "Scarab" }
              ]
            }
            """;

        using var harness = await SettingsHarness.StartedAsync(json);
        var watchlist = harness.Store.Current.Watchlist;

        Assert.Equal(2, watchlist.Count);
        Assert.Equal(new ItemId("divine"), watchlist[0].Id);
        Assert.Equal(ExchangeCategory.Currency, watchlist[0].Category.Known);
        Assert.Equal(new ItemId("chaos"), watchlist[1].Id);
    }

    [Fact]
    public async Task SE8_OutOfRangeNumbers_AreClampedAndRecorded()
    {
        const string low = """
            {
              "schemaVersion": 1, "refreshIntervalMinutes": 1,
              "window": { "opacity": 0.05, "width": 10, "height": 99999, "x": 5, "y": 5 }
            }
            """;

        using var harness = await SettingsHarness.StartedAsync(low);
        var settings = harness.Store.Current;

        Assert.Equal(5, settings.RefreshIntervalMinutes);
        // 0.5, not 0.2: below α=128 the subpixel spread ClearType carries has never been measured,
        // and it is already halved there (00-shell-measurements.md §11.3, SettingsValidation.MinOpacity).
        Assert.Equal(0.5, settings.Window.Opacity);
        Assert.Equal(240, settings.Window.Width);
        Assert.Equal(4000, settings.Window.Height);

        // The position is only checked for finiteness: whether the point is on a monitor is Shell's
        // question, not this module's.
        Assert.Equal(5, settings.Window.X);

        var corrections = Assert.IsType<SettingsLoadResult.Loaded>(harness.Store.LastLoadResult).Corrections;
        Assert.Contains("refreshIntervalMinutes", corrections);
        Assert.Contains("window.opacity", corrections);
        Assert.Contains("window.width", corrections);
        Assert.Contains("window.height", corrections);
    }

    [Fact]
    public async Task SE8_TheUpperBounds_AreClampedToo()
    {
        const string high = """
            { "schemaVersion": 1, "refreshIntervalMinutes": 999, "window": { "opacity": 2.0 } }
            """;

        using var harness = await SettingsHarness.StartedAsync(high);

        Assert.Equal(60, harness.Store.Current.RefreshIntervalMinutes);
        Assert.Equal(1.0, harness.Store.Current.Window.Opacity);
    }

    [Fact]
    public async Task ANonFinitePosition_FallsBackToTheDefault()
    {
        // JSON has no NaN literal, so a non-finite value can only arrive as a non-number.
        const string json = """{ "schemaVersion": 1, "window": { "x": "left", "y": 40 } }""";

        using var harness = await SettingsHarness.StartedAsync(json);

        Assert.Equal(WindowSettings.Default.X, harness.Store.Current.Window.X);
        Assert.Equal(40, harness.Store.Current.Window.Y);
    }

    [Fact]
    public async Task SE9_AFutureSchemaVersion_IsReadOnlyAndNotQuarantined()
    {
        const string json = """
            { "schemaVersion": 2, "league": "Allflame", "watchlist": [ { "id": "divine", "category": "Currency" } ] }
            """;

        using var harness = await SettingsHarness.StartedAsync(json);

        Assert.Equal(WriteBlockReason.FutureSchema, harness.Store.BlockReason);
        Assert.True(harness.Sink.StateOf(AppConditionKind.SettingsReadOnly));

        // Nothing is moved aside: the file is perfectly good, just newer than this build.
        Assert.True(File.Exists(harness.FilePath));
        Assert.Empty(harness.QuarantineFiles());

        // What could be read is still shown, so the user keeps seeing their own watchlist.
        Assert.Equal("Allflame", harness.Store.Current.League);
        Assert.Single(harness.Store.Current.Watchlist);
    }

    [Fact]
    public async Task AMissingOrOlderSchemaVersion_IsTreatedAsOne()
    {
        using var missing = await SettingsHarness.StartedAsync("""{ "league": "Allflame" }""");
        using var older = await SettingsHarness.StartedAsync("""{ "schemaVersion": 0, "league": "Allflame" }""");

        Assert.Equal(WriteBlockReason.None, missing.Store.BlockReason);
        Assert.Equal(WriteBlockReason.None, older.Store.BlockReason);
        Assert.Equal(1, missing.Store.Current.SchemaVersion);
    }

    [Fact]
    public async Task SE10_AnUnreadableFile_RaisesItsOwnCondition()
    {
        using var harness = SettingsHarness.Create("""{ "schemaVersion": 1 }""");

        using (var _ = new FileStream(harness.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await harness.Store.StartingAsync(CancellationToken.None);
        }

        Assert.Equal(WriteBlockReason.Unreadable, harness.Store.BlockReason);
        Assert.True(harness.Sink.StateOf(AppConditionKind.SettingsUnreadable));

        // Nothing was quarantined, because nothing could be touched — which is why this needed a
        // condition of its own. Without one the user rebuilds a watchlist, watches it apply, and
        // loses all of it at the next start-up with no warning at any point.
        Assert.Empty(harness.QuarantineFiles());
        Assert.Null(harness.Sink.StateOf(AppConditionKind.SettingsCorrupt));
        Assert.Null(harness.Sink.StateOf(AppConditionKind.SettingsWriteFailed));
    }

    [Fact]
    public async Task ABlankLeague_BecomesNullRatherThanAnEmptyString()
    {
        using var harness = await SettingsHarness.StartedAsync("""{ "schemaVersion": 1, "league": "   " }""");

        Assert.Null(harness.Store.Current.League);
    }

    [Fact]
    public async Task AMalformedLanguageTag_FallsBackToEnglish()
    {
        using var harness = await SettingsHarness.StartedAsync("""{ "schemaVersion": 1, "language": "not a tag" }""");
        using var good = await SettingsHarness.StartedAsync("""{ "schemaVersion": 1, "language": "zh-Hans" }""");

        Assert.Equal("en", harness.Store.Current.Language);
        Assert.Equal("zh-Hans", good.Store.Current.Language);
    }

    [Fact]
    public async Task ANonBooleanFirstRunFlag_IsFalseWithoutACorrection()
    {
        using var harness = await SettingsHarness.StartedAsync(
            """{ "schemaVersion": 1, "firstRunAcknowledged": "yes" }""");

        Assert.False(harness.Store.Current.FirstRunAcknowledged);
        Assert.Empty(Assert.IsType<SettingsLoadResult.Loaded>(harness.Store.LastLoadResult).Corrections);
    }

    [Fact]
    public async Task LoadingNeverRewritesTheFile()
    {
        const string json = """{ "schemaVersion": 1, "refreshIntervalMinutes": 999 }""";
        using var harness = await SettingsHarness.StartedAsync(json);

        // Silently normalising on start-up would erase whatever the user was in the middle of
        // hand-editing.
        Assert.Equal(json, harness.ReadFile());
    }
}
