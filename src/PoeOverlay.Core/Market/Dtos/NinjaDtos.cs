using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoeOverlay.Core.Market.Dtos;

/// <summary>
/// The response skeleton of the exchange overview endpoint (S2 5.2 / S4 7.1).
/// </summary>
/// <remarks>
/// <para>
/// Every member is a property, never a field: System.Text.Json ignores fields entirely without
/// <c>IncludeFields=true</c>, so a field-shaped DTO deserialises to all-default values while
/// compiling and running without complaint.
/// </para>
/// <para>
/// Every skeleton member is nullable because <c>required</c> does not stop a JSON <c>null</c> —
/// <c>{"core":null}</c> deserialises happily and the next member access throws
/// <see cref="NullReferenceException"/>, which is not a <see cref="JsonException"/> and therefore
/// escapes the failure-as-value contract. Step 2' of S2 5.5.3 consumes these nulls explicitly.
/// </para>
/// </remarks>
internal sealed class NinjaOverviewDto
{
    /// <summary>Response-global header: base currency, alternate currency and the item table.</summary>
    [JsonPropertyName("core")]
    public CoreDto? Core { get; init; }

    /// <summary>
    /// Received as raw elements so that each line can be deserialised independently (D-MK2).
    /// </summary>
    /// <remarks>
    /// Under <c>NumberHandling.Strict</c> a single <c>"primaryValue": "0.5"</c> kills the whole
    /// document — the healthy lines die with the malformed one and D8-b never gets a sample to
    /// measure. Element-wise deserialisation limits the blast radius to one line.
    /// </remarks>
    [JsonPropertyName("lines")]
    public JsonElement[]? Lines { get; init; }
}

/// <summary>The response-global header (contract A3: one base currency for the whole document).</summary>
internal sealed class CoreDto
{
    /// <summary>The base currency of every <c>primaryValue</c> in the document. Measured value: "chaos".</summary>
    [JsonPropertyName("primary")]
    public string? Primary { get; init; }

    /// <summary>The secondary conversion currency. Read but not mapped.</summary>
    [JsonPropertyName("secondary")]
    public string? Secondary { get; init; }

    /// <summary>An array, not a map (contract A2) — the join builds a dictionary once per response.</summary>
    [JsonPropertyName("items")]
    public CoreItemDto[]? Items { get; init; }

    // core.rates is deliberately absent (D-MK1). D1 forbids using the reciprocal, and a field that
    // does not exist on the type cannot be used by mistake.
}

/// <summary>One row of the item table, joined to a line by <c>id</c> (contract A1).</summary>
internal sealed class CoreItemDto
{
    /// <summary>Slug; the join key.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Display name. It exists nowhere in <c>lines</c>, which is why the join is mandatory.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Icon path, relative to https://poe.ninja. Read but not mapped.</summary>
    [JsonPropertyName("image")]
    public string? Image { get; init; }

    /// <summary>Self-reported category (contract A6).</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>poe.ninja detail slug. Read but not mapped.</summary>
    [JsonPropertyName("detailsId")]
    public string? DetailsId { get; init; }
}

/// <summary>One priced line (S4 7.1).</summary>
internal sealed class LineDto
{
    /// <summary>Slug.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Value in <c>core.primary</c> units.</summary>
    [JsonPropertyName("primaryValue")]
    public decimal? PrimaryValue { get; init; }

    /// <summary>Traded volume, in <c>core.primary</c> units.</summary>
    [JsonPropertyName("volumePrimaryValue")]
    public double? VolumePrimaryValue { get; init; }

    /// <summary>The most-traded counter currency; the sole basis of FR-04-3's auto mode.</summary>
    [JsonPropertyName("maxVolumeCurrency")]
    public string? MaxVolumeCurrency { get; init; }

    /// <summary>Held for cross-checking only; never an input to a calculation (FR-04-5, D1).</summary>
    [JsonPropertyName("maxVolumeRate")]
    public decimal? MaxVolumeRate { get; init; }

    /// <summary>Seven-point cumulative change series plus its total.</summary>
    [JsonPropertyName("sparkline")]
    public SparklineDto? Sparkline { get; init; }
}

/// <summary>Cumulative percentage change series (contract 3.2).</summary>
internal sealed class SparklineDto
{
    /// <summary>The change percentage the UI shows.</summary>
    [JsonPropertyName("totalChange")]
    public double? TotalChange { get; init; }

    /// <summary>Seven cumulative percentages. Read but not carried into Domain — no chart in scope 1.</summary>
    [JsonPropertyName("data")]
    public double[]? Data { get; init; }
}

/// <summary>One league of the league endpoint (contract 1.1).</summary>
internal sealed class LeagueDto
{
    /// <summary>League id; the value used in the <c>league=</c> query.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
