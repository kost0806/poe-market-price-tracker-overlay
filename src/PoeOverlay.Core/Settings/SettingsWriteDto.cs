using System.Text.Json.Serialization;

namespace PoeOverlay.Core.Settings;

/// <summary>
/// The shape actually written to <c>settings.json</c> (S4 10.7, D-DL15).
/// </summary>
/// <remarks>
/// Serialising <see cref="AppSettings"/> directly compiles and produces the wrong document:
/// <c>WatchlistEntry.Id</c> is an <c>ItemId</c>, <c>Category</c> a <c>CategoryRef</c> and
/// <c>DisplayCurrency</c> a nullable enum, so System.Text.Json emits
/// <c>{"id":{"value":"divine"},"category":{"raw":"Currency","known":1}}</c> — nested objects and
/// numeric enums where the S4 10.2 schema is flat and lower case. This DTO exists so the on-disk
/// contract is stated once, in one place, in the shape the contract actually has.
/// <para>
/// Every non-nullable reference property is <c>required</c>. Without it,
/// <c>WarningsAsErrors=Nullable</c> rejects the type outright (CS8618) and S2 1.6 forbids the
/// <c>null!</c> that would otherwise silence it.
/// </para>
/// </remarks>
internal sealed class SettingsWriteDto
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("league")]
    public string? League { get; init; }

    [JsonPropertyName("refreshIntervalMinutes")]
    public int RefreshIntervalMinutes { get; init; }

    [JsonPropertyName("language")]
    public required string Language { get; init; }

    [JsonPropertyName("defaultDisplayCurrency")]
    public required string DefaultDisplayCurrency { get; init; }

    [JsonPropertyName("window")]
    public required WindowWriteDto Window { get; init; }

    [JsonPropertyName("watchlist")]
    public required WatchlistEntryWriteDto[] Watchlist { get; init; }

    [JsonPropertyName("firstRunAcknowledged")]
    public bool FirstRunAcknowledged { get; init; }
}

/// <summary>Window geometry in the flat on-disk shape (S4 10.2 / 10.7).</summary>
internal sealed class WindowWriteDto
{
    [JsonPropertyName("x")]
    public double X { get; init; }

    [JsonPropertyName("y")]
    public double Y { get; init; }

    [JsonPropertyName("width")]
    public double Width { get; init; }

    [JsonPropertyName("height")]
    public double Height { get; init; }

    [JsonPropertyName("heightMode")]
    public required string HeightMode { get; init; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; init; }
}

/// <summary>One watchlist row in the flat on-disk shape (S4 10.2 / 10.7).</summary>
internal sealed class WatchlistEntryWriteDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>Omitted entirely when the entry inherits the global default (S2 4.1).</summary>
    [JsonPropertyName("displayCurrency")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayCurrency { get; init; }
}

/// <summary>
/// The domain-to-disk direction of the settings contract (S4 10.7).
/// </summary>
/// <remarks>
/// The mapping is total in both directions and has its own regression test: every field of
/// <see cref="AppSettings"/> reaches the document and every key of the document comes back.
/// <c>Category</c> carries <see cref="Domain.CategoryRef.Raw"/> verbatim, never
/// <c>Known?.ToString()</c>, so an unknown category the user typed survives a full round trip
/// rather than disappearing on the next save.
/// </remarks>
internal static class SettingsWriteDtoMapper
{
    public static SettingsWriteDto ToWriteDto(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SettingsWriteDto
        {
            SchemaVersion = settings.SchemaVersion,
            League = settings.League,
            RefreshIntervalMinutes = settings.RefreshIntervalMinutes,
            Language = settings.Language,
            DefaultDisplayCurrency = SettingsValidation.ToJsonValue(settings.DefaultDisplayCurrency),
            Window = new WindowWriteDto
            {
                X = settings.Window.X,
                Y = settings.Window.Y,
                Width = settings.Window.Width,
                Height = settings.Window.Height,
                HeightMode = SettingsValidation.ToJsonValue(settings.Window.HeightMode),
                Opacity = settings.Window.Opacity,
            },
            Watchlist = settings.Watchlist.Select(entry => new WatchlistEntryWriteDto
            {
                Id = entry.Id.ToString(),
                Category = entry.Category.Raw,
                DisplayCurrency = entry.DisplayCurrency is { } currency
                    ? SettingsValidation.ToJsonValue(currency)
                    : null,
            }).ToArray(),
            FirstRunAcknowledged = settings.FirstRunAcknowledged,
        };
    }
}
