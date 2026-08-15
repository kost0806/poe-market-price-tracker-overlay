using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoeOverlay.Core.Localization;

/// <summary>
/// Source-generated reader for the flat <c>Dictionary&lt;string, string&gt;</c> dictionary files
/// (S2 1.7 "lenient" / S4 5.4).
/// </summary>
/// <remarks>
/// The key <em>is</em> the JSON property name, so there is nothing to map. Lenient, unlike
/// <c>NinjaJsonContext</c>: a translator's trailing comma or comment must not cost a language.
/// A <see cref="JsonException"/> drops that one language and logs a warning.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocalizationJsonContext : JsonSerializerContext
{
}
