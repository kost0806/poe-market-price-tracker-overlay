using System.Globalization;
using Microsoft.Extensions.Logging;
using FontFamily = System.Windows.Media.FontFamily;
using Typeface = System.Windows.Media.Typeface;

namespace PoeOverlay.Overlay;

/// <summary>
/// Checks, once at start-up, that the bundled typeface is the one being drawn with (S3 4.9 D-SH22).
/// </summary>
/// <remarks>
/// <para>
/// An unresolved font reference is the quietest failure in WPF: the family is substituted, every
/// window still draws, and nothing anywhere says the app is not in the typeface it declared. This
/// app exists to tell <c>8</c> from <c>6</c> at 12px, so which face is on screen is not cosmetic.
/// </para>
/// <para>
/// The check runs against the same string the two XAML roots declare, and asserts nothing: it
/// reports. A missing typeface is a degraded appearance, not a reason to refuse to start.
/// </para>
/// </remarks>
internal static class BundledTypeface
{
    /// <summary>The family reference. Must stay identical to the two XAML <c>FontFamily</c> values.</summary>
    internal const string FamilyUri = "pack://application:,,,/Fonts/#Pretendard";

    /// <summary>The family name the bundled faces report.</summary>
    internal const string ExpectedFamily = "Pretendard";

    /// <summary>
    /// Reports which family the shipped reference actually resolves to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base URI is passed explicitly rather than folded into the string. 【measured】
    /// <c>new FontFamily("pack://application:,,,/Fonts/#Pretendard")</c> does <em>not</em> resolve —
    /// the string constructor has no base URI to resolve a resource against and falls back silently
    /// — while the identical string as a XAML attribute does, because the parser supplies the XAML's
    /// own base URI. Reading the shipped reference back therefore has to supply one too, or this
    /// check would report a failure the app does not have.
    /// </para>
    /// </remarks>
    /// <param name="logger">Where the result goes.</param>
    /// <returns>The resolved family name, or null when the reference did not resolve.</returns>
    internal static string? Verify(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var family = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#" + ExpectedFamily);
        var typeface = new Typeface(family, default, default, default);

        if (!typeface.TryGetGlyphTypeface(out var glyphs))
        {
            logger.LogWarning(
                "The bundled typeface {Family} did not resolve; the windows are drawing in a substituted font.",
                FamilyUri);
            return null;
        }

        var resolved = glyphs.FamilyNames.TryGetValue(new CultureInfo("en-us"), out var english)
            ? english
            : string.Join("/", glyphs.FamilyNames.Values);

        if (!string.Equals(resolved, ExpectedFamily, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "The bundled typeface reference resolved to {Resolved}, not {Expected}.",
                resolved,
                ExpectedFamily);
            return resolved;
        }

        logger.LogInformation(
            "Typeface {Resolved} resolved from the bundled resource ({Count} faces).",
            resolved,
            family.GetTypefaces().Count);

        return resolved;
    }
}
