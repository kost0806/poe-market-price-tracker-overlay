using System.Text.RegularExpressions;

namespace PoeOverlay.Core.Localization;

/// <summary>
/// Validates a dictionary file stem as a language tag (S2 3.2, widened form / S4 5.3).
/// </summary>
/// <remarks>
/// The first edition's <c>^[a-z]{2}(-[A-Z]{2})?$</c> rejected script subtags, so dropping
/// <c>zh-Hans.json</c> into the folder did nothing and FR-07-3's "no code change" was false for
/// those languages. Three-digit region codes and extension subtags remain a documented limit.
/// </remarks>
internal static partial class LanguageTagValidator
{
    /// <summary>True when <paramref name="fileStem"/> is a tag this app accepts.</summary>
    public static bool IsValid(string fileStem)
        => !string.IsNullOrEmpty(fileStem) && TagPattern().IsMatch(fileStem);

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Z][a-z]{3})?(-[A-Z]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
