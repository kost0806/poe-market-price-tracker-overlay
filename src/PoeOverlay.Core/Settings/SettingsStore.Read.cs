using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Settings;

/// <summary>
/// The manual <c>JsonDocument</c> read path and the quarantine rules (S2 8.4 / 8.7, S4 10.6).
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>S2 8.7 — at most this many quarantined files are kept; the oldest go first.</summary>
    internal const int MaxQuarantineFiles = 10;

    /// <summary>
    /// The six-step read of S2 8.4, in order.
    /// </summary>
    /// <remarks>
    /// Static because nothing here depends on instance state, but it takes the clock: S2 1.3 makes
    /// <c>TimeProvider</c> the only source of time, and the quarantine file name is a UTC stamp.
    /// The S4 10.6 signature omits the parameter, which cannot be implemented without reaching for
    /// <c>DateTimeOffset.UtcNow</c> directly.
    /// </remarks>
    internal static SettingsLoadResult Load(string path, TimeProvider timeProvider)
    {
        string text;
        try
        {
            // 1. No file is the ordinary first-run path, not a failure: the file appears on the
            //    first successful write.
            if (!File.Exists(path))
            {
                return new SettingsLoadResult.Defaulted(NoFileReason);
            }

            text = File.ReadAllText(path);
        }
#pragma warning disable CA1031 // S2 9.5 row 6: the observable results are a failure value plus SettingsUnreadable.
        catch (Exception ex)
        {
            // 2. Unreadable. Nothing is quarantined, because the file cannot be touched at all —
            //    which is precisely why this needs its own condition (D-SE1): it is neither a
            //    corruption (nothing was moved) nor a write failure (nothing was attempted).
            return new SettingsLoadResult.IoFailed(path, ex.GetType().Name);
        }
#pragma warning restore CA1031

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            // 3. Not JSON at all.
            return Quarantine(path, timeProvider);
        }

        using (document)
        {
            // 4. JSON, but not an object: no key can be read from it.
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Quarantine(path, timeProvider);
            }

            var root = document.RootElement;

            // 5. schemaVersion decides policy *before* the rest is read. A future file is shown but
            //    never written back, so a newer build's data is not silently downgraded.
            var schemaVersion = ReadSchemaVersion(root);
            var settings = ParseAndValidate(root, out var corrections);

            return schemaVersion > SettingsValidation.CurrentSchemaVersion
                ? new SettingsLoadResult.ReadOnly(settings)
                : new SettingsLoadResult.Loaded(settings, corrections);
        }
    }

    /// <summary>
    /// Reads every key and applies the S2 8.2 table.
    /// </summary>
    /// <remarks>
    /// Each key is independent: a bad <c>refreshIntervalMinutes</c> costs the user that one value,
    /// never the watchlist. That property is the entire reason this is not a deserialiser call.
    /// </remarks>
    internal static AppSettings ParseAndValidate(JsonElement root, out IReadOnlyList<string> corrections)
    {
        var notes = new List<string>();
        var defaults = AppSettings.Default;

        var league = SettingsValidation.NormalizeLeague(ReadString(root, "league"));

        var interval = defaults.RefreshIntervalMinutes;
        if (TryReadInt(root, "refreshIntervalMinutes", out var rawInterval))
        {
            interval = SettingsValidation.ClampRefreshInterval(rawInterval);
            if (interval != rawInterval)
            {
                notes.Add("refreshIntervalMinutes");
            }
        }
        else if (root.TryGetProperty("refreshIntervalMinutes", out _))
        {
            notes.Add("refreshIntervalMinutes");
        }

        var rawLanguage = ReadString(root, "language");
        var language = SettingsValidation.NormalizeLanguage(rawLanguage);
        if (rawLanguage is not null && !string.Equals(language, rawLanguage.Trim(), StringComparison.Ordinal))
        {
            notes.Add("language");
        }

        var displayCurrency = defaults.DefaultDisplayCurrency;
        if (root.TryGetProperty("defaultDisplayCurrency", out _))
        {
            if (!SettingsValidation.TryParseDisplayCurrency(ReadString(root, "defaultDisplayCurrency"), out displayCurrency))
            {
                notes.Add("defaultDisplayCurrency");
            }
        }

        var window = ParseWindow(root, notes);
        var watchlist = ParseWatchlist(root, notes);

        // A boolean that fails to parse is false, and that is not worth a correction note: the
        // default is the same value the key would have had if it were simply absent (S4 10.6).
        var firstRun = root.TryGetProperty("firstRunAcknowledged", out var firstRunElement)
            && firstRunElement.ValueKind == JsonValueKind.True;

        corrections = notes;
        return new AppSettings(
            SettingsValidation.CurrentSchemaVersion,
            league,
            interval,
            language,
            displayCurrency,
            window,
            watchlist,
            firstRun);
    }

    /// <summary>
    /// Maps one watchlist element, or discards it.
    /// </summary>
    /// <param name="discardReason">Non-null only for the single discard rule: a blank id.</param>
    internal static WatchlistEntry? ParseWatchlistEntry(JsonElement element, out string? discardReason)
    {
        discardReason = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            discardReason = "notAnObject";
            return null;
        }

        var rawId = ReadString(element, "id")?.Trim();
        if (string.IsNullOrEmpty(rawId))
        {
            // The only discard rule in the whole table (S2 8.2). An entry with no id names nothing
            // and cannot be repaired by the user, because there is nothing on the row to recognise.
            discardReason = "id";
            return null;
        }

        var category = SettingsValidation.ParseCategory(ReadString(element, "category"));

        DisplayCurrency? currency = null;
        if (element.TryGetProperty("displayCurrency", out var currencyElement)
            && currencyElement.ValueKind == JsonValueKind.String
            && SettingsValidation.TryParseDisplayCurrency(currencyElement.GetString(), out var parsed))
        {
            currency = parsed;
        }

        return new WatchlistEntry(new ItemId(rawId), category, currency);
    }

    private static int ReadSchemaVersion(JsonElement root)
        => TryReadInt(root, "schemaVersion", out var version)
            ? version
            : SettingsValidation.CurrentSchemaVersion;

    private static WindowSettings ParseWindow(JsonElement root, List<string> notes)
    {
        var defaults = WindowSettings.Default;
        if (!root.TryGetProperty("window", out var window) || window.ValueKind != JsonValueKind.Object)
        {
            if (root.TryGetProperty("window", out _))
            {
                notes.Add("window");
            }

            return defaults;
        }

        var x = ReadDouble(window, "x", defaults.X, notes, "window.x", SettingsValidation.SanitizePosition);
        var y = ReadDouble(window, "y", defaults.Y, notes, "window.y", SettingsValidation.SanitizePosition);
        var width = ReadDouble(window, "width", defaults.Width, notes, "window.width", SettingsValidation.ClampExtent);
        var height = ReadDouble(window, "height", defaults.Height, notes, "window.height", SettingsValidation.ClampExtent);

        var heightMode = defaults.HeightMode;
        if (window.TryGetProperty("heightMode", out _)
            && !SettingsValidation.TryParseHeightMode(ReadString(window, "heightMode"), out heightMode))
        {
            notes.Add("window.heightMode");
        }

        var opacity = defaults.Opacity;
        if (window.TryGetProperty("opacity", out var opacityElement))
        {
            if (opacityElement.ValueKind == JsonValueKind.Number && opacityElement.TryGetDouble(out var rawOpacity))
            {
                opacity = SettingsValidation.ClampOpacity(rawOpacity);
                if (!opacity.Equals(rawOpacity))
                {
                    notes.Add("window.opacity");
                }
            }
            else
            {
                notes.Add("window.opacity");
            }
        }

        return new WindowSettings(x, y, width, height, heightMode, opacity);
    }

    private static double ReadDouble(
        JsonElement parent,
        string key,
        double fallback,
        List<string> notes,
        string note,
        Func<double, double, double> sanitize)
    {
        if (!parent.TryGetProperty(key, out var element))
        {
            return fallback;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var raw))
        {
            notes.Add(note);
            return fallback;
        }

        var value = sanitize(raw, fallback);
        if (!value.Equals(raw))
        {
            notes.Add(note);
        }

        return value;
    }

    private static EquatableArray<WatchlistEntry> ParseWatchlist(JsonElement root, List<string> notes)
    {
        if (!root.TryGetProperty("watchlist", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            if (root.TryGetProperty("watchlist", out _))
            {
                notes.Add("watchlist");
            }

            return new EquatableArray<WatchlistEntry>([]);
        }

        var entries = new List<WatchlistEntry>();
        var seen = new HashSet<ItemId>();
        var index = 0;

        foreach (var element in array.EnumerateArray())
        {
            var entry = ParseWatchlistEntry(element, out var discardReason);
            if (entry is null)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"watchlist[{index}].{discardReason}"));
                index++;
                continue;
            }

            // First occurrence wins and insertion order is preserved, so an accidental duplicate
            // does not reorder the user's list (S2 8.2).
            if (!seen.Add(entry.Id))
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"watchlist[{index}].duplicate"));
                index++;
                continue;
            }

            entries.Add(entry);
            index++;
        }

        return new EquatableArray<WatchlistEntry>(entries);
    }

    private static string? ReadString(JsonElement parent, string key)
        => parent.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool TryReadInt(JsonElement parent, string key, out int value)
    {
        value = 0;
        return parent.TryGetProperty(key, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    /// <summary>
    /// Moves the unreadable document aside and prunes the archive (S2 8.7).
    /// </summary>
    /// <remarks>
    /// <c>File.Move</c>, not copy-then-delete: a failure halfway through a copy leaves two copies
    /// of a file that is already suspect. The name is a UTC stamp so ordinal sort order equals
    /// chronological order, with an ordinal suffix for two corruptions in the same second.
    /// </remarks>
    private static SettingsLoadResult Quarantine(string path, TimeProvider timeProvider)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var stamp = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        var target = Path.Combine(directory, $"settings.corrupt-{stamp}.json");
        var ordinal = 2;
        while (File.Exists(target))
        {
            target = Path.Combine(directory, $"settings.corrupt-{stamp}-{ordinal.ToString(CultureInfo.InvariantCulture)}.json");
            ordinal++;
        }

        try
        {
            File.Move(path, target);
            PruneQuarantine(directory);
        }
#pragma warning disable CA1031 // S2 9.5 row 6: the observable result is the Corrupt value itself.
        catch (Exception)
        {
            // The quarantine attempt failed; the document is still corrupt and writes are still
            // blocked, which is what the returned value says. Reporting a different outcome here
            // would tell the user the file was saved when it was not.
        }
#pragma warning restore CA1031

        return new SettingsLoadResult.Corrupt(target);
    }

    private static void PruneQuarantine(string directory)
    {
        var files = Directory.GetFiles(directory, "settings.corrupt-*.json");
        if (files.Length <= MaxQuarantineFiles)
        {
            return;
        }

        var ordered = files.OrderBy(QuarantineSortKey, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < ordered.Length - MaxQuarantineFiles; i++)
        {
            File.Delete(ordered[i]);
        }
    }

    /// <summary>
    /// Orders quarantine files chronologically.
    /// </summary>
    /// <remarks>
    /// A plain ordinal sort of the file names is not chronological for two corruptions in the same
    /// second: <c>'-'</c> sorts before <c>'.'</c>, so <c>…Z-2.json</c> comes before <c>…Z.json</c>
    /// and pruning would delete the newer of the pair. Normalising the absent suffix to <c>01</c>
    /// makes the ordinal comparison mean what S2 8.7 says it means.
    /// </remarks>
    internal static string QuarantineSortKey(string path)
    {
        const string prefix = "settings.corrupt-";

        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return name;
        }

        var rest = name[prefix.Length..];
        var dash = rest.IndexOf('-', StringComparison.Ordinal);

        // The stamp itself contains no dash, so the first one can only introduce the collision
        // ordinal.
        return dash < 0
            ? rest + "-01"
            : rest[..dash] + "-" + rest[(dash + 1)..].PadLeft(2, '0');
    }

    private void ApplyLoadResult(SettingsLoadResult result)
    {
        var now = _timeProvider.GetUtcNow();

        switch (result)
        {
            case SettingsLoadResult.Loaded loaded:
                Volatile.Write(ref _current, loaded.Settings);
                if (loaded.Corrections.Count > 0)
                {
                    // Corrections are recorded but the file is not rewritten (S2 8.2): a start-up
                    // that silently normalised the user's file would erase whatever they were in
                    // the middle of hand-editing.
                    Log(
                        LogLevel.Information,
                        "SettingsCorrected",
                        $"Settings loaded with corrections: {string.Join(", ", loaded.Corrections)}.");
                }

                break;

            case SettingsLoadResult.Defaulted:
                Volatile.Write(ref _current, AppSettings.Default);
                break;

            case SettingsLoadResult.IoFailed failed:
                SetBlockReason(WriteBlockReason.Unreadable);
                _conditionSink.Set(AppConditionKind.SettingsUnreadable, true, failed.Path);
                _errorSink.Report(new ErrorRecord(
                    now, "Settings", "SettingsUnreadable", "ui.error.generic",
                    failed.Path, null, null, null, failed.ExceptionType));
                Log(LogLevel.Error, "SettingsUnreadable", $"Could not read {failed.Path} ({failed.ExceptionType}).");
                break;

            case SettingsLoadResult.Corrupt corrupt:
                SetBlockReason(WriteBlockReason.Corrupt);
                _conditionSink.Set(AppConditionKind.SettingsCorrupt, true, corrupt.QuarantinePath);
                _errorSink.Report(new ErrorRecord(
                    now, "Settings", "SettingsCorrupt", "ui.error.settingsCorrupt",
                    corrupt.QuarantinePath, null, null, null, null));
                Log(LogLevel.Error, "SettingsCorrupt", $"Settings were corrupt and moved to {corrupt.QuarantinePath}.");
                break;

            case SettingsLoadResult.ReadOnly readOnly:
                Volatile.Write(ref _current, readOnly.Settings);
                SetBlockReason(WriteBlockReason.FutureSchema);
                _conditionSink.Set(AppConditionKind.SettingsReadOnly, true, null);
                Log(LogLevel.Warning, "SettingsReadOnly", "The settings file has a newer schema version; writes are disabled.");
                break;

            default:
                throw new NotSupportedException($"Unhandled settings load result {result.GetType().Name}.");
        }
    }

    /// <summary>Reports and clears the breadcrumb left by a failed shutdown flush (S2 8.6, D17).</summary>
    private void ReportFlushFailureTrace()
    {
        try
        {
            if (!File.Exists(FlushFailureTracePath))
            {
                return;
            }

            var content = File.ReadAllText(FlushFailureTracePath).Trim();
            Log(
                LogLevel.Warning,
                "SettingsFlushFailureTrace",
                $"The previous session could not save settings on shutdown (at {content}).");

            // Reported exactly once, then removed, so the warning tracks the last shutdown rather
            // than every shutdown since the first failure.
            File.Delete(FlushFailureTracePath);
        }
#pragma warning disable CA1031 // S2 9.5 row 6: the observable result is a Warning entry.
        catch (Exception ex)
        {
            Log(LogLevel.Warning, "SettingsFlushFailureTrace", "Could not read the flush-failure trace file.", ex);
        }
#pragma warning restore CA1031
    }
}
