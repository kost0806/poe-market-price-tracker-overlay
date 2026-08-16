using System.Text.Json;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Settings;
using Xunit;

namespace PoeOverlay.Core.Tests.Settings;

/// <summary>
/// S4 16.6 — the write DTO and its mapper, character for character against the S4 10.2 key table.
/// </summary>
/// <remarks>
/// Serialising <see cref="AppSettings"/> directly compiles and produces the wrong document:
/// <c>{"id":{"value":"divine"},"category":{"raw":"Currency","known":1}}</c> — nested objects where
/// the schema is flat, numeric enums where it is lower-case text. Nothing but a test comparing the
/// emitted keys against the table catches that, because it is not a compile error.
/// </remarks>
public sealed class SettingsWriteDtoMapperTests
{
    private static AppSettings Sample() => new(
        SchemaVersion: 1,
        League: "Allflame",
        RefreshIntervalMinutes: 15,
        Language: "ko",
        DefaultDisplayCurrency: DisplayCurrency.Divine,
        Window: new WindowSettings(12.5, -30, 420, 500, HeightMode.Explicit, 0.87),
        Watchlist: new EquatableArray<WatchlistEntry>(
        [
            new WatchlistEntry(new ItemId("divine"), new CategoryRef("Currency", ExchangeCategory.Currency), DisplayCurrency.Chaos),
            new WatchlistEntry(new ItemId("chisel"), new CategoryRef("Chisel", null), null),
            new WatchlistEntry(new ItemId("rusted"), new CategoryRef("Scarab", ExchangeCategory.Scarab), DisplayCurrency.Auto),
        ]),
        FirstRunAcknowledged: true);

    private static string Serialize(AppSettings settings)
        => JsonSerializer.Serialize(
            SettingsWriteDtoMapper.ToWriteDto(settings), SettingsJsonContext.Default.SettingsWriteDto);

    [Fact]
    public void EveryTopLevelKey_MatchesTheSchemaTable()
    {
        using var document = JsonDocument.Parse(Serialize(Sample()));

        Assert.Equal(
            new[]
            {
                "schemaVersion", "league", "refreshIntervalMinutes", "language",
                "defaultDisplayCurrency", "window", "watchlist", "firstRunAcknowledged",
            },
            document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void EveryWindowKey_MatchesTheSchemaTable()
    {
        using var document = JsonDocument.Parse(Serialize(Sample()));
        var window = document.RootElement.GetProperty("window");

        Assert.Equal(
            new[] { "x", "y", "width", "height", "heightMode", "opacity" },
            window.EnumerateObject().Select(p => p.Name).ToArray());

        Assert.Equal(12.5, window.GetProperty("x").GetDouble());
        Assert.Equal(-30, window.GetProperty("y").GetDouble());
        Assert.Equal("explicit", window.GetProperty("heightMode").GetString());
        Assert.Equal(0.87, window.GetProperty("opacity").GetDouble());
    }

    [Fact]
    public void EnumsAreWrittenAsLowerCaseTextRatherThanNumbers()
    {
        using var document = JsonDocument.Parse(Serialize(Sample()));

        Assert.Equal("divine", document.RootElement.GetProperty("defaultDisplayCurrency").GetString());
        Assert.Equal("explicit", document.RootElement.GetProperty("window").GetProperty("heightMode").GetString());
    }

    [Fact]
    public void WatchlistEntriesAreFlatAndKeepTheirRawCategoryToken()
    {
        using var document = JsonDocument.Parse(Serialize(Sample()));
        var entries = document.RootElement.GetProperty("watchlist").EnumerateArray().ToArray();

        Assert.Equal(3, entries.Length);

        Assert.Equal("divine", entries[0].GetProperty("id").GetString());
        Assert.Equal("Currency", entries[0].GetProperty("category").GetString());
        Assert.Equal("chaos", entries[0].GetProperty("displayCurrency").GetString());

        // An unknown category the user typed survives a full round trip rather than disappearing at
        // the next save: the mapper writes CategoryRef.Raw, never Known?.ToString().
        Assert.Equal("Chisel", entries[1].GetProperty("category").GetString());

        // An omitted display currency stays omitted, because "omitted" and "explicitly auto" are
        // different facts (S2 4.1).
        Assert.False(entries[1].TryGetProperty("displayCurrency", out _));
        Assert.Equal("auto", entries[2].GetProperty("displayCurrency").GetString());
    }

    [Fact]
    public void ANullLeagueIsWrittenAsNullRatherThanOmitted()
    {
        using var document = JsonDocument.Parse(Serialize(Sample() with { League = null }));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("league").ValueKind);
    }

    [Fact]
    public async Task TheMappingIsTotalInBothDirections()
    {
        var original = Sample();
        var json = Serialize(original);

        using var harness = await SettingsHarness.StartedAsync(json);

        // Every field survives, including the unknown category, the omitted display currency and the
        // explicit Auto that must not be confused with it.
        Assert.Equal(original, harness.Store.Current);
        Assert.Empty(Assert.IsType<SettingsLoadResult.Loaded>(harness.Store.LastLoadResult).Corrections);
    }

    [Fact]
    public async Task TheDefaultSettings_AlsoSurviveARoundTrip()
    {
        using var harness = await SettingsHarness.StartedAsync(Serialize(AppSettings.Default));

        Assert.Equal(AppSettings.Default, harness.Store.Current);
    }

    [Fact]
    public void TheWrittenDocument_IsIndentedForPeopleToRead()
    {
        Assert.Contains("\n", Serialize(Sample()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaults_MatchTheConstantTable()
    {
        var defaults = AppSettings.Default;

        Assert.Equal(1, defaults.SchemaVersion);
        Assert.Null(defaults.League);
        Assert.Equal(5, defaults.RefreshIntervalMinutes);
        Assert.Equal("en", defaults.Language);
        Assert.Equal(DisplayCurrency.Auto, defaults.DefaultDisplayCurrency);
        Assert.Equal(new WindowSettings(100, 100, 420, 500, HeightMode.Auto, 0.87), defaults.Window);
        Assert.Empty(defaults.Watchlist);
        Assert.False(defaults.FirstRunAcknowledged);
    }
}
