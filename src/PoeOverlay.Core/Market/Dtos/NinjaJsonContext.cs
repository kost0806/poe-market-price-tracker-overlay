using System.Text.Json.Serialization;

namespace PoeOverlay.Core.Market.Dtos;

/// <summary>
/// The source-generated, read-only, strict context for every poe.ninja payload (S2 5.3 / S4 7.2).
/// </summary>
/// <remarks>
/// <para>
/// "Strict" here means "no lenient conversion", not "reject unknown members": poe.ninja adding a
/// field is normal evolution, so <see cref="JsonUnmappedMemberHandling.Skip"/> is deliberate. What
/// must be caught is a field that <em>disappears</em> or changes type.
/// </para>
/// <para>
/// All five options are .NET 8 defaults, so writing them down fixes nothing by itself — the real
/// risk is somebody reaching for <c>JsonSerializerDefaults.Web</c>, which flips
/// <c>PropertyNameCaseInsensitive</c> and silently destroys D8-b's ability to notice a renamed
/// field. M22 asserts the generated <c>Options</c> instance directly.
/// </para>
/// <para>
/// Three roots are enough: the skeleton is parsed once, each line element is parsed individually
/// through <see cref="LineDto"/>, and the league endpoint returns an array.
/// <c>CoreDto</c>/<c>CoreItemDto</c>/<c>SparklineDto</c> are reached through those graphs.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    NumberHandling = JsonNumberHandling.Strict,
    AllowTrailingCommas = false,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Disallow,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(NinjaOverviewDto))]
[JsonSerializable(typeof(LineDto))]
[JsonSerializable(typeof(LeagueDto[]))]
internal sealed partial class NinjaJsonContext : JsonSerializerContext
{
}
