using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoeOverlay.Overlay;

/// <summary>
/// Source-generated reader for the flat slug → file-name manifest (S4 12.7).
/// </summary>
/// <remarks>
/// The same shape and the same leniency as <c>LocalizationJsonContext</c>: the key <em>is</em> the
/// JSON property name, and a stray trailing comma in a generated file must cost a picture rather
/// than throw on a path with no user in front of it.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class IconManifestJsonContext : JsonSerializerContext
{
}
