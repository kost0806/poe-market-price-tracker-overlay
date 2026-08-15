using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Localization;

/// <summary>
/// The five-level fallback chain and its dictionaries (S2 3 / S4 5.2).
/// </summary>
/// <remarks>
/// <para>
/// Every dictionary is read once, at <see cref="StartingAsync"/> (D-L1). Two reasons: level ② always
/// needs the on-disk <c>en</c>, and D10 recomputes every string on the UI thread when the language
/// changes — that path must contain no file I/O.
/// </para>
/// <para>
/// Levels: ① current language, ② on-disk <c>en</c>, ③ embedded <c>en</c> (the floor, which cannot be
/// absent), ④ the API name (item-name space only), ⑤ the key itself. Level ⑤ printing a raw key is a
/// diagnostic for state strings and a loss of function for prices — which is why <c>Pricing</c> and
/// <c>Presentation</c> hold compile-time constants and reach this class through
/// <see cref="ITemplateSource"/> rather than <see cref="Ui"/>.
/// </para>
/// </remarks>
public sealed class LocalizationCatalog : ILocalizer, IHostedLifecycleService
{
    /// <summary>The embedded floor's tag; also the fallback when a request cannot be honoured.</summary>
    public const string DefaultLanguage = "en";

    /// <summary>Manifest name of the embedded floor dictionary (S2 3.3, D3).</summary>
    public const string EmbeddedResourceName = "PoeOverlay.Core.Localization.en.json";

    /// <summary>Suppression channel for level ⑤, keyed by (language, space, key) — S4 14.8.</summary>
    public const string UnresolvedKeyChannel = "loc.unresolvedKey";

    /// <summary>Suppression channel for level ④, keyed by (language, slug) — S4 14.8.</summary>
    public const string ItemNameFallbackChannel = "loc.itemNameFallback";

    /// <summary>Suppression channel for S2 3.7 violations, keyed by (language, key) — S4 14.8.</summary>
    public const string TemplatePlaceholderChannel = "loc.templatePlaceholder";

    private readonly string _baseDirectory;
    private readonly ILogger<LocalizationCatalog> _logger;
    private readonly SessionSuppressionRegistry _suppression;

    private FrozenDictionary<string, FrozenDictionary<string, string>> _byTag =
        FrozenDictionary<string, FrozenDictionary<string, string>>.Empty;

    private FrozenDictionary<string, string> _diskEn = FrozenDictionary<string, string>.Empty;
    private FrozenDictionary<string, string> _embeddedEn = FrozenDictionary<string, string>.Empty;
    private IReadOnlyList<LanguageInfo> _languages = [new LanguageInfo(DefaultLanguage, DefaultLanguage)];
    private string _currentLanguage = DefaultLanguage;

    /// <summary>
    /// Creates a catalog reading dictionaries from <paramref name="baseDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Composition passes <c>{AppContext.BaseDirectory}/Localization/</c> (S2 3.2); this class never
    /// reads <c>AppContext</c> itself so tests can point it anywhere.
    /// </remarks>
    public LocalizationCatalog(
        string baseDirectory,
        ILogger<LocalizationCatalog> logger,
        SessionSuppressionRegistry suppression)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(suppression);

        _baseDirectory = baseDirectory;
        _logger = logger;
        _suppression = suppression;
    }

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <inheritdoc />
    public IReadOnlyList<LanguageInfo> Languages => Volatile.Read(ref _languages);

    /// <inheritdoc />
    public string CurrentLanguage => Volatile.Read(ref _currentLanguage);

    /// <summary>Loads every dictionary and runs the S2 3.7 placeholder check (D-L1).</summary>
    public Task StartingAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Load();
        return Task.CompletedTask;
    }

    /// <summary>No-op; loading happens in <see cref="StartingAsync"/>.</summary>
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>No-op.</summary>
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>No-op.</summary>
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>No-op.</summary>
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>No-op.</summary>
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public bool TryGetTemplate(string key, out string template)
    {
        if (string.IsNullOrEmpty(key))
        {
            template = string.Empty;
            return false;
        }

        var value = Resolve(key, apiName: null, isItemName: false, out var level);
        if (level <= 3)
        {
            template = value;
            return true;
        }

        template = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public string Ui(string key, params string[] args)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var template = Resolve(key, apiName: null, isItemName: false, out _);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            // The template is a translator's, so a mismatch is data, not a programming error.
            // The observable result is the unformatted template (S2 9.5).
            return template;
        }
    }

    /// <inheritdoc />
    public string ItemName(ItemId id, string? apiName)
    {
        var slug = id.Value;
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.IsNullOrWhiteSpace(apiName) ? string.Empty : apiName;
        }

        return Resolve(slug, apiName, isItemName: true, out _);
    }

    /// <inheritdoc />
    public void SetLanguage(string tag)
    {
        var requested = tag;
        if (string.IsNullOrWhiteSpace(requested) || !HasLanguage(requested))
        {
            _logger.LogWarning(
                "Requested language '{Tag}' was not discovered; falling back to '{Fallback}'.",
                requested,
                DefaultLanguage);
            requested = DefaultLanguage;
        }

        if (string.Equals(requested, CurrentLanguage, StringComparison.Ordinal))
        {
            return;
        }

        // Published first, then announced (S2 3.5) — a handler must never observe the old language.
        Volatile.Write(ref _currentLanguage, requested);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The chain itself. <paramref name="level"/> is the level that answered (① to ⑤) and exists so
    /// tests can pin the branch rather than the wording (S2 11.6 L1–L10).
    /// </summary>
    internal string Resolve(string key, string? apiName, bool isItemName, out int level)
    {
        var current = CurrentLanguage;

        // ① The current language, unless it *is* en — then ① and ② are the same table (L7).
        if (!string.Equals(current, DefaultLanguage, StringComparison.Ordinal)
            && _byTag.TryGetValue(current, out var currentDict)
            && Hit(currentDict, key, out var fromCurrent))
        {
            level = 1;
            return fromCurrent;
        }

        // ② The deployed en.json, which a translator may be editing.
        if (Hit(_diskEn, key, out var fromDiskEn))
        {
            level = 2;
            return fromDiskEn;
        }

        // ③ The embedded floor.
        if (Hit(_embeddedEn, key, out var fromEmbeddedEn))
        {
            level = 3;
            return fromEmbeddedEn;
        }

        // ④ The API name — item-name space only; ui.* keys have no such concept.
        if (isItemName && !string.IsNullOrWhiteSpace(apiName))
        {
            level = 4;
            if (_suppression.ShouldReport(ItemNameFallbackChannel, SuppressionKey(current, key)))
            {
                _logger.LogDebug(
                    "Item name '{Slug}' is not translated in '{Language}'; using the API name.",
                    key,
                    current);
            }

            return apiName;
        }

        // ⑤ The key itself.
        level = 5;
        var space = isItemName ? "ItemName" : "Ui";
        if (_suppression.ShouldReport(UnresolvedKeyChannel, SuppressionKey(current, space, key)))
        {
            _logger.LogWarning(
                "Key '{Key}' ({Space}) is unresolved in '{Language}'; rendering the key itself.",
                key,
                space,
                current);
        }

        return key;
    }

    private static bool Hit(FrozenDictionary<string, string> dictionary, string key, out string value)
    {
        // A translator's "key": "" is an unfilled slot, not a hit (S2 3.4).
        if (dictionary.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    // The language is part of every suppression key, so switching languages reports the same
    // unresolved key again for the new language (S2 3.4, L8).
    private static string SuppressionKey(string language, string key)
        => string.Concat(language, " ", key);

    private static string SuppressionKey(string language, string space, string key)
        => string.Concat(language, " ", space, " ", key);

    private bool HasLanguage(string tag)
    {
        foreach (var language in Languages)
        {
            if (string.Equals(language.Tag, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void Load()
    {
        _embeddedEn = Freeze(DefaultLanguage + " (embedded)", ReadEmbedded());

        var byTag = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (tag, entries) in ReadDirectory())
        {
            byTag[tag] = Freeze(tag, entries);
        }

        _byTag = byTag.ToFrozenDictionary(StringComparer.Ordinal);
        _diskEn = _byTag.TryGetValue(DefaultLanguage, out var diskEn)
            ? diskEn
            : FrozenDictionary<string, string>.Empty;

        _languages = BuildLanguages();
    }

    private IReadOnlyList<LanguageInfo> BuildLanguages()
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal) { DefaultLanguage };
        foreach (var tag in _byTag.Keys)
        {
            tags.Add(tag);
        }

        var languages = new List<LanguageInfo>(tags.Count);
        foreach (var tag in tags)
        {
            languages.Add(new LanguageInfo(tag, SelfName(tag)));
        }

        return languages;
    }

    private string SelfName(string tag)
    {
        if (_byTag.TryGetValue(tag, out var dict) && Hit(dict, UiKeyCatalog.SelfNameKey, out var name))
        {
            return name;
        }

        if (string.Equals(tag, DefaultLanguage, StringComparison.Ordinal)
            && Hit(_embeddedEn, UiKeyCatalog.SelfNameKey, out var embedded))
        {
            return embedded;
        }

        // CultureInfo is deliberately not consulted (S2 3.2) — the tag is its own display name.
        return tag;
    }

    private Dictionary<string, string>? ReadEmbedded()
    {
        var assembly = typeof(LocalizationCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            _logger.LogError(
                "The embedded dictionary '{Resource}' is missing; the fallback chain has no floor.",
                EmbeddedResourceName);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                stream,
                LocalizationJsonContext.Default.DictionaryStringString);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "The embedded dictionary '{Resource}' could not be parsed; the fallback chain has no floor.",
                EmbeddedResourceName);
            return null;
        }
    }

    private IEnumerable<(string Tag, Dictionary<string, string>? Entries)> ReadDirectory()
    {
        string[] files;
        try
        {
            files = Directory.Exists(_baseDirectory)
                ? Directory.GetFiles(_baseDirectory, "*.json")
                : [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not enumerate '{Directory}'; no dictionaries were loaded.", _baseDirectory);
            yield break;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not enumerate '{Directory}'; no dictionaries were loaded.", _baseDirectory);
            yield break;
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            var tag = Path.GetFileNameWithoutExtension(file);
            if (!LanguageTagValidator.IsValid(tag))
            {
                _logger.LogWarning("Ignoring '{File}': '{Tag}' is not a language tag this app accepts.", file, tag);
                continue;
            }

            var entries = ReadFile(file);
            if (entries is null)
            {
                continue;
            }

            yield return (tag, entries);
        }
    }

    private Dictionary<string, string>? ReadFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.DictionaryStringString);
        }
        catch (JsonException ex)
        {
            // One bad file costs one language, never the app (S2 3.2).
            _logger.LogWarning(ex, "Ignoring '{File}': it is not valid JSON.", path);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Ignoring '{File}': it could not be read.", path);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Ignoring '{File}': it could not be read.", path);
            return null;
        }
    }

    /// <summary>
    /// Freezes one dictionary, dropping the <c>ui.*</c> entries whose placeholders disagree with the
    /// central table (S2 3.7, D-L3).
    /// </summary>
    /// <remarks>
    /// Dropping the entry is what makes the fallback chain descend. The check runs here, at load,
    /// rather than only in Pricing's per-render nets, because those nets record nothing: a
    /// translator's <c>"{0}c ({2}d)"</c> would fall back silently and forever, and where the
    /// constant and the translation are the same string there would be no symptom at all.
    /// </remarks>
    private FrozenDictionary<string, string> Freeze(string tag, Dictionary<string, string>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return FrozenDictionary<string, string>.Empty;
        }

        var kept = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (key is null || value is null)
            {
                continue;
            }

            if (UiKeyCatalog.IsUiKey(key)
                && UiKeyCatalog.TryGetArgumentCount(key, out var expected)
                && !PlaceholdersAgree(value, expected))
            {
                if (_suppression.ShouldReport(TemplatePlaceholderChannel, SuppressionKey(tag, key)))
                {
                    _logger.LogWarning(
                        "Dropping '{Key}' from '{Tag}': the template '{Template}' does not use exactly {Expected} placeholder(s).",
                        key,
                        tag,
                        value,
                        expected);
                }

                continue;
            }

            kept[key] = value;
        }

        return kept.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="template"/> uses <c>{0}</c>…<c>{n-1}</c> and nothing beyond,
    /// recognising <c>{{</c>/<c>}}</c> escapes.
    /// </summary>
    /// <remarks>
    /// Formatting with unique sentinels answers all three questions at once — an escaped
    /// <c>{{0}}</c> leaves no sentinel behind, a missing slot leaves its sentinel behind, and an
    /// index past the end throws. Scanning for the literal text <c>{0}</c> answers none of them.
    /// </remarks>
    private static bool PlaceholdersAgree(string template, int expected)
    {
        var sentinels = new object[expected];
        for (var i = 0; i < expected; i++)
        {
            sentinels[i] = Sentinel(i);
        }

        string formatted;
        try
        {
            formatted = string.Format(CultureInfo.InvariantCulture, template, sentinels);
        }
        catch (FormatException)
        {
            return false;
        }

        for (var i = 0; i < expected; i++)
        {
            if (!formatted.Contains(Sentinel(i), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Sentinel(int index)
        => string.Concat("\u0001", index.ToString(CultureInfo.InvariantCulture), "\u0002");
}
