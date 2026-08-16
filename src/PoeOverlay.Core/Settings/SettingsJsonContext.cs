using System.Text.Json.Serialization;

namespace PoeOverlay.Core.Settings;

/// <summary>
/// The write-only serialisation context for settings (S2 1.7 / S4 10.8).
/// </summary>
/// <remarks>
/// Write only. Reading goes through manual <c>JsonDocument</c> traversal instead, because a
/// deserialiser cannot preserve partial validity — one bad <c>refreshIntervalMinutes</c> would
/// cost the user their whole watchlist — cannot keep unknown categories, cannot tell a corrupt
/// document from a valid document holding a silly value, and cannot decide policy from
/// <c>schemaVersion</c> before reading the rest (S2 8.4). Writing has none of those problems.
/// <para><c>WriteIndented</c> because this is a file people open and edit by hand (S2 8.5).</para>
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsWriteDto))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
