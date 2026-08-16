using System.Globalization;
using System.Text.Json;
using PoeOverlay.Core.Localization;
using Xunit;

namespace PoeOverlay.Core.Tests.Localization;

/// <summary>
/// S4 16.2 (제7판) — every dictionary this project ships, not just the embedded English one.
/// </summary>
/// <remarks>
/// <para>
/// <c>PriceTemplateFallbackTests</c> C1 already holds <c>en.json</c> to the S4 14 catalogue. It
/// reads the embedded resource, so it says nothing about any other language — and the way a
/// translated dictionary fails is not a crash. A key it omits falls through to English and a key
/// whose placeholders disagree is dropped at load with one warning (S2 3.7 D-L3). Both leave a
/// working app with one string in the wrong language, which nobody notices.
/// </para>
/// <para>
/// The files are read from the source tree rather than the output directory on purpose: what has
/// to be correct is what is committed, and a dictionary that failed to be copied would otherwise
/// pass by being absent.
/// </para>
/// </remarks>
public sealed class ShippedDictionaryTests
{
    /// <summary>
    /// Keys deliberately left untranslated, with the reason they cannot be filled in.
    /// </summary>
    /// <remarks>
    /// Neither GGG static response contains "Djinn" in any form, so there is no measured Korean
    /// term for the category, and poe.ninja lists no items under that type. Inventing one would be
    /// indistinguishable from a verified term and nothing would mark it for revisiting — the same
    /// reason <c>00-api-contract.md</c> §6.4 forbids filling unresolved slugs with English. This
    /// set is spelt out so that growing it is a deliberate act with a test to change.
    /// </remarks>
    private static readonly Dictionary<string, HashSet<string>> Untranslated = new(StringComparer.Ordinal)
    {
        ["ko"] = new(StringComparer.Ordinal) { "ui.category.djinnCoin" },
    };

    [Fact]
    public void TheKoreanDictionaryIsShipped()
    {
        // FR-07-3's "no code change" is only true if the file is actually there; a rename or a
        // dropped csproj entry costs the whole language and nothing else fails.
        Assert.Contains("ko", ShippedTags());
    }

    [Fact]
    public void EveryShippedDictionary_AnswersEveryCataloguedKey()
    {
        foreach (var (tag, entries) in ShippedDictionaries())
        {
            var exempt = Untranslated.TryGetValue(tag, out var set)
                ? set
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var key in UiKeyCatalog.Keys)
            {
                if (exempt.Contains(key))
                {
                    continue;
                }

                Assert.True(
                    entries.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
                    $"{tag}.json has no usable value for {key}; that string would silently stay English");
            }
        }
    }

    [Fact]
    public void EveryDeliberateOmission_IsStillOmitted()
    {
        // The other direction of the exemption: once a term can be measured, the entry lands in
        // the dictionary and this list must shrink. Without this the list outlives its reason.
        foreach (var (tag, exempt) in Untranslated)
        {
            var entries = ShippedDictionaries()[tag];
            foreach (var key in exempt)
            {
                Assert.False(
                    entries.ContainsKey(key),
                    $"{tag}.json now has {key}; drop it from the exemption list and say where the term was measured");
            }
        }
    }

    [Fact]
    public void NoShippedDictionary_CarriesAnUncataloguedUiKey()
    {
        foreach (var (tag, entries) in ShippedDictionaries())
        {
            foreach (var key in entries.Keys)
            {
                if (!UiKeyCatalog.IsUiKey(key))
                {
                    continue;
                }

                // A mistyped key is inert: it resolves nothing and shadows nothing, so the string
                // it was meant to translate quietly stays English.
                Assert.True(
                    UiKeyCatalog.TryGetArgumentCount(key, out _),
                    $"{tag}.json has {key}, which is not in the S4 14 catalogue");
            }
        }
    }

    [Fact]
    public void EveryShippedTemplate_UsesExactlyTheCataloguedArgumentCount()
    {
        foreach (var (tag, entries) in ShippedDictionaries())
        {
            foreach (var (key, value) in entries)
            {
                if (!UiKeyCatalog.TryGetArgumentCount(key, out var expected))
                {
                    continue;
                }

                Assert.True(
                    ConsumesExactly(value, expected),
                    $"{tag}.json's {key} = \"{value}\" does not use exactly {expected} placeholder(s); "
                    + "LocalizationCatalog would drop it at load and the app would fall back to English");
            }
        }
    }

    [Fact]
    public void EveryNonUiKey_LooksLikeAPoeNinjaSlug()
    {
        foreach (var (tag, entries) in ShippedDictionaries())
        {
            foreach (var key in entries.Keys)
            {
                if (UiKeyCatalog.IsUiKey(key))
                {
                    continue;
                }

                // The two key spaces share one file and are told apart by the prefix alone
                // (S2 3.1), so anything not starting with "ui." is claimed to be an item slug.
                // poe.ninja's ids are lower-case kebab — all 968 of them, checked. A key that is
                // neither shape is a generator accident, and it would resolve as nothing: no
                // interface string uses it and no item id ever equals it.
                Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", key);
                Assert.False(
                    string.IsNullOrWhiteSpace(entries[key]),
                    $"{tag}.json maps {key} to blank, which the chain treats as an unfilled slot (S2 3.4)");
            }
        }
    }

    /// <summary>The same sentinel technique <c>LocalizationCatalog.PlaceholdersAgree</c> uses.</summary>
    private static bool ConsumesExactly(string template, int expected)
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

    private static IReadOnlyList<string> ShippedTags()
        => [.. ShippedDictionaries().Keys];

    private static Dictionary<string, Dictionary<string, string>> ShippedDictionaries()
    {
        var directory = Path.Combine(
            RepositoryRoot(), "src", "PoeOverlay.Core", "Localization", "Localization");

        var dictionaries = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            var tag = Path.GetFileNameWithoutExtension(file);
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))
                ?? throw new InvalidOperationException($"{file} is not a flat string dictionary");

            dictionaries[tag] = entries;
        }

        return dictionaries;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PoeOverlay.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"no PoeOverlay.sln above {AppContext.BaseDirectory}; these tests read the committed dictionaries");
    }
}
