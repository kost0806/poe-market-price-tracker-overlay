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
    /// <summary>Response-global header: base currency, alternate currency and the rate basis.</summary>
    [JsonPropertyName("core")]
    public CoreDto? Core { get; init; }

    /// <summary>
    /// The name table — one entry per line, and the only source of item names (contract §2.0, A1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document root carries three keys, not two. The first implementation declared only
    /// <c>core</c> and <c>lines</c> and read names out of <c>core.items</c>, which is the rate basis
    /// and holds exactly <c>[chaos, divine]</c>: 2 of 959 lines joined, so nearly every row rendered
    /// as its slug.
    /// </para>
    /// <para>
    /// Nullable and <em>not</em> demanded by the skeleton null check of step 2'. A missing name table
    /// costs the fallback chain its fourth rung and nothing else (S2 5.4); the observable is
    /// <see cref="Domain.CategorySnapshot.JoinMissCount"/>, which then equals the line count.
    /// </para>
    /// </remarks>
    [JsonPropertyName("items")]
    public ItemDto[]? Items { get; init; }

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

    /// <summary>
    /// The rate basis — exactly <c>[chaos, divine]</c> in all 18 categories, never the name table.
    /// </summary>
    /// <remarks>
    /// An array, not a map (contract A2). Its <c>category</c> is the one that equals the query
    /// <c>type</c>, which is why contract A6's self-describing check reads this array and not the
    /// root one (S2 5.4).
    /// </remarks>
    [JsonPropertyName("items")]
    public ItemDto[]? Items { get; init; }

    // core.rates is deliberately absent (D-MK1). D1 forbids using the reciprocal, and a field that
    // does not exist on the type cannot be used by mistake.
}

/// <summary>
/// One row of either item array, joined to a line by <c>id</c> (contract A1/A2).
/// </summary>
/// <remarks>
/// The two arrays share an element shape and share this type, but they are different things: the
/// root <c>items</c> is the name table and <c>core.items</c> is the rate basis (contract §2.0).
/// </remarks>
internal sealed class ItemDto
{
    /// <summary>Slug; the join key.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Display name. It exists nowhere in <c>lines</c>, which is why the join is mandatory.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Icon path, relative to https://poe.ninja. Read but not mapped, and absent on every
    /// DivinationCard entry (576 of 959 carry one) — nothing may assume it is there.
    /// </summary>
    [JsonPropertyName("image")]
    public string? Image { get; init; }

    /// <summary>
    /// Self-reported category. Only <c>core.items</c>' copy equals the query <c>type</c> and so
    /// answers contract A6; the root array's copy is a display grouping (<c>Fragments</c>,
    /// <c>Cards</c>, …) and would disagree on nearly every response.
    /// </summary>
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
