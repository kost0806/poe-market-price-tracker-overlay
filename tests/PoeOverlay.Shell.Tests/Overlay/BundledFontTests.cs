using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using PoeOverlay.Overlay;
using Xunit;

namespace PoeOverlay.Shell.Tests.Overlay;

/// <summary>
/// The typeface really is inside the assembly, at the path the XAML's pack URI names, and it really
/// is the full Korean Pretendard (S3 4.7).
/// </summary>
/// <remarks>
/// <para>
/// Every way this breaks is silent. Drop the <c>Resource</c> items from the csproj, rename the
/// folder, or swap in the Latin-only "Pretendard Std" build, and WPF substitutes a fallback face
/// without a word — the windows still draw, in the wrong font, and Korean would come out as boxes.
/// </para>
/// <para>
/// The assertions go through the font bytes rather than through
/// <c>new FontFamily("pack://application:,,,/Fonts/#Pretendard")</c>, which cannot be exercised
/// here: that URI resolves against <c>Application.ResourceAssembly</c>, which is the entry assembly,
/// and under a test host the entry assembly is the runner. Its setter is one-shot and has already
/// fired by the time any test body runs (measured: it throws). What the shipped URI resolves to in
/// the app itself is a run-time observation, recorded in <c>00-shell-measurements.md</c> §13.
/// </para>
/// </remarks>
public sealed class BundledFontTests
{
    /// <summary>
    /// The resource keys the pack URI's <c>/Fonts/</c> folder is made of. WPF lower-cases resource
    /// paths, so these are the exact strings a rename would change.
    /// </summary>
    private static readonly string[] Expected =
    [
        "fonts/pretendard-regular.otf",
        "fonts/pretendard-semibold.otf",
    ];

    /// <summary>
    /// The unpacked faces, written complete before any of them is opened.
    /// </summary>
    /// <remarks>
    /// All of them, in one go, because WPF caches a folder's listing the first time it opens a font
    /// from it: a second face written afterwards is not in that listing, and constructing it throws
    /// NullReferenceException from inside the rasteriser (measured — the first face loads, the
    /// second does not, and nothing in the failure says why). The folder is emptied once per run
    /// rather than per test, since the mapping WPF holds outlives the test that made it.
    /// </remarks>
    private static readonly Lazy<string> Scratch = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "PoeOverlay.Shell.Tests.Fonts");
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);

        using var reader = OpenResources();
        foreach (var key in Expected)
        {
            reader.GetResourceData(key, out _, out var data);
            File.WriteAllBytes(Path.Combine(path, Path.GetFileName(key)), data[SignatureOffset(data)..]);
        }

        return path;
    });

    [Fact]
    public void BothWeightsAreEmbedded_UnderTheFolderThePackUriNames()
    {
        var keys = ResourceKeys();

        Assert.All(Expected, e => Assert.Contains(e, keys));
    }

    [Fact]
    public void TheEmbeddedBytesAreTheRealPretendard_AndCoverKorean()
    {
        // FR-07-3's dictionary is still empty, so nothing on screen is Korean yet. The subset
        // question has to be settled now regardless: finding out when the dictionary lands would
        // mean choosing the font twice.
        foreach (var key in Expected)
        {
            var glyphs = Load(key);

            Assert.Contains("Pretendard", glyphs.FamilyNames.Values);

            // First and last syllables of the Hangul Syllables block, and a compatibility jamo.
            Assert.True(glyphs.CharacterToGlyphMap.ContainsKey('가'), $"{key}: 가 is missing.");
            Assert.True(glyphs.CharacterToGlyphMap.ContainsKey('힣'), $"{key}: 힣 is missing.");
            Assert.True(glyphs.CharacterToGlyphMap.ContainsKey('ㄱ'), $"{key}: ㄱ is missing.");
        }
    }

    [Fact]
    public void TheTwoFacesAreRegularAndSemiBold()
    {
        // A weight WPF cannot find is a weight WPF synthesises, and the settings window's group
        // headers stop being distinguishable from their contents without saying so.
        Assert.Equal(FontWeights.Normal, Load(Expected[0]).Weight);
        Assert.Equal(FontWeights.SemiBold, Load(Expected[1]).Weight);
    }

    private static HashSet<string> ResourceKeys()
    {
        using var reader = OpenResources();

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in reader)
        {
            keys.Add((string)entry.Key);
        }

        return keys;
    }

    /// <summary>Opens one unpacked face; a <see cref="GlyphTypeface"/> is addressed by URI.</summary>
    private static GlyphTypeface Load(string key)
        => new(new Uri(Path.Combine(Scratch.Value, Path.GetFileName(key))));

    /// <summary>Where the font itself starts: the <c>OTTO</c> tag of an OpenType/CFF face.</summary>
    private static int SignatureOffset(byte[] data)
    {
        for (var i = 0; i < 8; i++)
        {
            if (data[i] == (byte)'O' && data[i + 1] == (byte)'T' && data[i + 2] == (byte)'T' && data[i + 3] == (byte)'O')
            {
                return i;
            }
        }

        throw new InvalidOperationException("The embedded resource does not begin with an OpenType/CFF signature.");
    }

    private static ResourceReader OpenResources()
    {
        var stream = typeof(OverlayView).Assembly.GetManifestResourceStream("PoeOverlay.g.resources")
            ?? throw new InvalidOperationException("The shell assembly carries no WPF resource set at all.");

        return new ResourceReader(stream);
    }
}
