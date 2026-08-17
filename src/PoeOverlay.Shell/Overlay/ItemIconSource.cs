using System.Collections.Frozen;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Overlay;

/// <summary>
/// Slug to picture, for the overlay's icon column (FR-04-6 / HLD D23 / S3 4.10.2 / S4 12.7).
/// </summary>
/// <remarks>
/// <para>
/// The manifest is a generated artefact — <c>tools/build-icon-manifest.py</c> writes it from the
/// same join that produces the Korean names (<c>00-api-contract.md</c> §6.3). It ships beside the
/// exe rather than inside the assembly because it is 5.3 MB of art that changes every league and a
/// user must be able to drop a new file into the folder (HLD D23).
/// </para>
/// <para>
/// Nothing here reaches the network. The icons are local files; a missing one costs a picture and
/// nothing else, which is why every failure below is a value rather than an exception.
/// </para>
/// <para>
/// UI thread only. The bitmaps are frozen, so the objects themselves would survive being handed
/// across threads, but the caches are plain dictionaries and the only caller is a value converter
/// running in a layout pass.
/// </para>
/// </remarks>
internal sealed class ItemIconSource
{
    /// <summary>The generated manifest's file name, inside the icon directory.</summary>
    internal const string ManifestFileName = "item-icons.json";

    private readonly string _iconDirectory;
    private readonly ILogger<ItemIconSource> _logger;

    /// <summary>Resolved icons, and the failures — a cached <c>null</c> is the "do not retry" mark.</summary>
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);

    private FrozenDictionary<string, string>? _manifest;

    /// <summary>Builds a source over <paramref name="iconDirectory"/>. Reads nothing yet.</summary>
    /// <param name="iconDirectory">Where the manifest and the PNGs live; <c>{BaseDirectory}/Icons</c>.</param>
    /// <param name="logger">Records the load result and the first failure of each slug.</param>
    internal ItemIconSource(string iconDirectory, ILogger<ItemIconSource> logger)
    {
        ArgumentNullException.ThrowIfNull(iconDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _iconDirectory = iconDirectory;
        _logger = logger;
    }

    /// <summary>How many slugs the manifest mapped, or -1 before it has been read.</summary>
    /// <remarks>The observable result of loading, so a test can tell "empty" from "not yet read".</remarks>
    internal int MappedCount => _manifest?.Count ?? -1;

    /// <summary>
    /// The icon for an item, or <see langword="null"/> when there is not one.
    /// </summary>
    /// <param name="id">The poe.ninja slug the row was built from.</param>
    /// <returns>A frozen <see cref="ImageSource"/>, or <see langword="null"/> — never throws.</returns>
    /// <remarks>
    /// <see langword="null"/> is a normal answer, not an error: divination cards aside, some items
    /// simply have no art in the source data (<c>00-api-contract.md</c> §6.6). The view keeps the
    /// column's width either way so the names stay aligned (S3 4.10.3).
    /// </remarks>
    internal ImageSource? Resolve(ItemId id)
    {
        var slug = id.Value;
        if (string.IsNullOrEmpty(slug))
        {
            return null;
        }

        // The failure is cached as well as the success. The overlay redraws every row on every
        // pass, so a slug whose file is missing would otherwise be opened again every 30 seconds.
        if (_cache.TryGetValue(slug, out var cached))
        {
            return cached;
        }

        var loaded = Load(slug);
        _cache[slug] = loaded;
        return loaded;
    }

    private ImageSource? Load(string slug)
    {
        var manifest = _manifest ??= ReadManifest();
        if (!manifest.TryGetValue(slug, out var fileName))
        {
            return null;
        }

        var path = Path.Combine(_iconDirectory, fileName);

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();

            // OnLoad, or the file stays mapped for the life of the process and the next league's
            // icon pull cannot overwrite it. S4 16.10 hit exactly that with the bundled font.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (
            ex is IOException
                or NotSupportedException
                or FileFormatException
                or UriFormatException
                or ArgumentException
                or COMException)
        {
            // COMException is in the list because WIC's failures do not all arrive wrapped: most
            // corrupt files surface as NotSupportedException or FileFormatException, some come
            // straight out of the codec. Enumerated rather than catch-all, which CA1031 makes an
            // error (S4 2.2) and which would also swallow the programming errors above it.
            // Once per slug per session: the cached null above is what stops this repeating.
            _logger.LogWarning(
                ex,
                "Could not load the icon {File} for '{Slug}'; the row is drawn without one.",
                fileName,
                slug);
            return null;
        }
    }

    private FrozenDictionary<string, string> ReadManifest()
    {
        var path = Path.Combine(_iconDirectory, ManifestFileName);

        try
        {
            using var stream = File.OpenRead(path);
            var entries = JsonSerializer.Deserialize(stream, IconManifestJsonContext.Default.DictionaryStringString);
            if (entries is null)
            {
                _logger.LogWarning("The icon manifest {Path} is JSON null; no icons will be drawn.", path);
                return FrozenDictionary<string, string>.Empty;
            }

            var accepted = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
            var rejected = 0;
            foreach (var (slug, fileName) in entries)
            {
                // The manifest names files inside the icon directory and nothing else. A value
                // carrying a separator is a defect in the generator, not an instruction to follow.
                if (string.IsNullOrWhiteSpace(fileName)
                    || fileName.Contains('/', StringComparison.Ordinal)
                    || fileName.Contains('\\', StringComparison.Ordinal)
                    || fileName.Contains("..", StringComparison.Ordinal))
                {
                    rejected++;
                    continue;
                }

                accepted[slug] = fileName;
            }

            if (rejected > 0)
            {
                _logger.LogWarning(
                    "Dropped {Count} icon manifest entry/entries whose file name was not a plain name.",
                    rejected);
            }

            _logger.LogDebug("Icon manifest loaded: {Count} slug(s) from {Path}.", accepted.Count, path);
            return accepted.ToFrozenDictionary(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Every row loses its icon and keeps its price. That is the observable result (D15).
            _logger.LogWarning(ex, "Could not read the icon manifest {Path}; no icons will be drawn.", path);
            return FrozenDictionary<string, string>.Empty;
        }
    }
}
