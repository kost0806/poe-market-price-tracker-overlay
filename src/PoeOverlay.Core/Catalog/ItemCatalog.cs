using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Catalog;

/// <summary>One catalogue row — an item that exists in this league, priced or not (S2 §6.8).</summary>
/// <param name="Id">The poe.ninja slug, which is the identity everywhere else too (FR-01-5).</param>
/// <param name="Category">Which exchange overview lists it. Without this a hit cannot be watched.</param>
/// <param name="EnglishName">The name the API would have returned. The only English name the app ships.</param>
public sealed record CatalogEntry(ItemId Id, ExchangeCategory Category, string EnglishName);

/// <summary>
/// Every item this league has, whether or not its prices have been fetched
/// (FR-01-1 / HLD D7 / S2 §6.8 / <c>00-api-contract.md</c> §6.8).
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately <em>not</em> part of the <c>Store</c>. Matching a query against a
/// localised name needs <c>Localization</c>, which the Store may not reference (S2 §1.2); the
/// catalogue carries no <c>(league, dataEpoch)</c> tag because it is a file rather than a fetch;
/// and the Store has no file I/O to speak of. <c>Presentation</c> merges the two (S3 §5.4.6).
/// </para>
/// <para>
/// Without it the app could ship a thousand item names and still be unable to find any of them:
/// a watchlist entry carries a category and prices are fetched per category, so a name on its own
/// is not something the user can act on.
/// </para>
/// <para>
/// A missing or broken file is not a failure worth stopping for. The observable result is that
/// search falls back to what it did before — the fetched cache only — and one warning is logged.
/// </para>
/// </remarks>
public sealed class ItemCatalog
{
    /// <summary>The generated file's name, inside the catalogue directory.</summary>
    public const string FileName = "item-catalog.json";

    private readonly string _directory;
    private readonly ILogger<ItemCatalog> _logger;

    private IReadOnlyList<CatalogEntry>? _entries;
    private Dictionary<ItemId, CatalogEntry>? _byId;

    /// <summary>Builds a catalogue over <paramref name="catalogDirectory"/>. Reads nothing yet.</summary>
    /// <param name="catalogDirectory">Where the generated file lives; <c>{BaseDirectory}/Catalog</c>.</param>
    /// <param name="logger">Records the load result and, once, a failure to read.</param>
    public ItemCatalog(string catalogDirectory, ILogger<ItemCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(catalogDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _directory = catalogDirectory;
        _logger = logger;
    }

    /// <summary>How many rows the file held, or -1 before it has been read.</summary>
    /// <remarks>
    /// "Not read yet" and "read, and empty" are different states, and the deferred load is only
    /// observable while they stay different — the same rule <c>ItemIconSource</c> follows.
    /// </remarks>
    public int Count => _entries?.Count ?? -1;

    /// <summary>Every row, in file order. Empty when the file is missing or unreadable.</summary>
    public IReadOnlyList<CatalogEntry> Entries => _entries ??= Read();

    /// <summary>Looks one slug up.</summary>
    /// <param name="id">The slug to find.</param>
    /// <param name="entry">The row, when there is one.</param>
    /// <returns><see langword="true"/> when the catalogue knows this slug.</returns>
    public bool TryGet(ItemId id, out CatalogEntry entry)
    {
        _byId ??= BuildIndex(Entries);

        if (_byId.TryGetValue(id, out var found))
        {
            entry = found;
            return true;
        }

        entry = new CatalogEntry(id, ExchangeCategory.Currency, string.Empty);
        return false;
    }

    private static Dictionary<ItemId, CatalogEntry> BuildIndex(IReadOnlyList<CatalogEntry> entries)
    {
        var index = new Dictionary<ItemId, CatalogEntry>(entries.Count);
        foreach (var entry in entries)
        {
            index[entry.Id] = entry;
        }

        return index;
    }

    private IReadOnlyList<CatalogEntry> Read()
    {
        var path = Path.Combine(_directory, FileName);

        try
        {
            using var stream = File.OpenRead(path);
            var rows = JsonSerializer.Deserialize(stream, CatalogJsonContext.Default.DictionaryStringCatalogEntryDto);
            if (rows is null)
            {
                _logger.LogWarning("The item catalogue {Path} is JSON null; search will see fetched data only.", path);
                return [];
            }

            var entries = new List<CatalogEntry>(rows.Count);
            var dropped = 0;

            foreach (var (slug, row) in rows)
            {
                // The generator refuses to write any of these (contract §6.8.2). They are checked
                // again because the file sits beside the exe and a user may swap it for a newer
                // league's — the same reason the icon manifest is validated on read (HLD D23).
                if (string.IsNullOrWhiteSpace(slug)
                    || row is null
                    || string.IsNullOrWhiteSpace(row.En)
                    || !Enum.TryParse<ExchangeCategory>(row.Cat, ignoreCase: false, out var category)
                    || !Enum.IsDefined(category))
                {
                    dropped++;
                    continue;
                }

                entries.Add(new CatalogEntry(new ItemId(slug), category, row.En));
            }

            if (dropped > 0)
            {
                _logger.LogWarning("Dropped {Count} item catalogue row(s) that named no usable category or name.", dropped);
            }

            _logger.LogDebug("Item catalogue loaded: {Count} row(s) from {Path}.", entries.Count, path);
            return entries;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Search keeps working over the fetched cache, which is exactly what it did before the
            // catalogue existed. That is the observable result (D15).
            _logger.LogWarning(ex, "Could not read the item catalogue {Path}; search will see fetched data only.", path);
            return [];
        }
    }
}
