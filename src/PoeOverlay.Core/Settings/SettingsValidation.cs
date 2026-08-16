using System.Globalization;
using System.Text.RegularExpressions;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Settings;

/// <summary>
/// The clamp-and-correct rules of S2 8.2, as pure functions (S4 2.1 <c>SettingsValidation.cs</c>).
/// </summary>
/// <remarks>
/// The whole table corrects; exactly one rule discards, and that is an entry whose id is blank.
/// An unknown category and an unknown display currency are <em>preserved</em>, never defaulted:
/// collapsing an unknown category would lose the user's typing on the next save, and forcing an
/// unknown display currency to <c>auto</c> would destroy the distinction between an explicit
/// <see cref="DisplayCurrency.Auto"/> and an omitted value that S2 4.1 depends on.
/// </remarks>
public static partial class SettingsValidation
{
    /// <summary>The only schema version this build writes or fully trusts.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>HLD 7 / D11.</summary>
    public const int DefaultRefreshIntervalMinutes = 5;

    /// <summary>S2 8.2 — the closed interval the refresh interval is clamped into.</summary>
    public const int MinRefreshIntervalMinutes = 5;

    /// <inheritdoc cref="MinRefreshIntervalMinutes"/>
    public const int MaxRefreshIntervalMinutes = 60;

    /// <summary>S2 8.2 — window width and height are clamped into this closed interval.</summary>
    public const double MinWindowExtent = 240d;

    /// <inheritdoc cref="MinWindowExtent"/>
    public const double MaxWindowExtent = 4000d;

    /// <summary>
    /// S2 8.2 / S4 15.1 — opacity is clamped into this closed interval.
    /// </summary>
    /// <remarks>
    /// The floor is 0.5 and not 0.2 because the alpha is not free. Lowering it attenuates
    /// ClearType: at α=128 the fringe count is unchanged (2,732 pixels, 90.8%) but the average
    /// subpixel spread halves, 55.74 against 111.11 (<c>00-shell-measurements.md</c> §11.3). α=128
    /// is 0.502 of the range, and it is the lowest alpha at which legibility has actually been
    /// measured; below it the design would be extrapolating on a surface whose only purpose is
    /// telling `8` from `6` at 12px. The floor is set at the measurement, not past it.
    /// </remarks>
    public const double MinOpacity = 0.5d;

    /// <inheritdoc cref="MinOpacity"/>
    public const double MaxOpacity = 1.0d;

    /// <summary>The language used when the stored tag is missing or malformed.</summary>
    public const string DefaultLanguage = "en";

    /// <summary>Trims, and turns blank into null (S2 8.2).</summary>
    public static string? NormalizeLeague(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Clamps into <c>[5, 60]</c>.</summary>
    public static int ClampRefreshInterval(int raw)
        => Math.Clamp(raw, MinRefreshIntervalMinutes, MaxRefreshIntervalMinutes);

    /// <summary>Clamps into <c>[0.5, 1.0]</c>; a non-finite value becomes the default. See <see cref="MinOpacity"/>.</summary>
    public static double ClampOpacity(double raw)
        => double.IsFinite(raw) ? Math.Clamp(raw, MinOpacity, MaxOpacity) : WindowSettings.Default.Opacity;

    /// <summary>Clamps into <c>[240, 4000]</c>; a non-finite value becomes <paramref name="fallback"/>.</summary>
    public static double ClampExtent(double raw, double fallback)
        => double.IsFinite(raw) ? Math.Clamp(raw, MinWindowExtent, MaxWindowExtent) : fallback;

    /// <summary>
    /// A window position is only checked for finiteness (S2 8.2).
    /// </summary>
    /// <remarks>Whether the point is on a monitor is Shell's question, not this module's.</remarks>
    public static double SanitizePosition(double raw, double fallback)
        => double.IsFinite(raw) ? raw : fallback;

    /// <summary>
    /// Accepts a language tag by shape only, falling back to <see cref="DefaultLanguage"/>.
    /// </summary>
    /// <remarks>
    /// S2 8.2 words the rule as "one of the discovered dictionaries", but S2 1.2 forbids
    /// <c>Settings → Localization</c>, so this module cannot see the discovered set. Shape is the
    /// strongest check available here; picking the actual dictionary — and falling back to the
    /// built-in floor when the requested one is missing — is Localization's five-level chain.
    /// </remarks>
    public static string NormalizeLanguage(string? raw)
    {
        var trimmed = raw?.Trim();
        return !string.IsNullOrEmpty(trimmed) && LanguageTagPattern().IsMatch(trimmed)
            ? trimmed
            : DefaultLanguage;
    }

    /// <summary>Parses <c>auto</c> / <c>chaos</c> / <c>divine</c>. Anything else is not a display currency.</summary>
    public static bool TryParseDisplayCurrency(string? raw, out DisplayCurrency value)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "auto":
                value = DisplayCurrency.Auto;
                return true;
            case "chaos":
                value = DisplayCurrency.Chaos;
                return true;
            case "divine":
                value = DisplayCurrency.Divine;
                return true;
            default:
                value = DisplayCurrency.Auto;
                return false;
        }
    }

    /// <summary>The inverse of <see cref="TryParseDisplayCurrency"/>; lower case, per the S4 10.2 key table.</summary>
    public static string ToJsonValue(DisplayCurrency value) => value switch
    {
        DisplayCurrency.Chaos => "chaos",
        DisplayCurrency.Divine => "divine",
        _ => "auto",
    };

    /// <summary>Parses <c>auto</c> / <c>explicit</c>.</summary>
    public static bool TryParseHeightMode(string? raw, out HeightMode value)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "auto":
                value = HeightMode.Auto;
                return true;
            case "explicit":
                value = HeightMode.Explicit;
                return true;
            default:
                value = HeightMode.Auto;
                return false;
        }
    }

    /// <summary>The inverse of <see cref="TryParseHeightMode"/>; lower case, per the S4 10.2 key table.</summary>
    public static string ToJsonValue(HeightMode value)
        => value == HeightMode.Explicit ? "explicit" : "auto";

    /// <summary>
    /// Turns a stored category token into a <see cref="CategoryRef"/>, preserving unknown tokens.
    /// </summary>
    /// <remarks>
    /// The round-trip check is not decoration. <c>Enum.TryParse</c> happily accepts <c>"1"</c> and
    /// returns <see cref="ExchangeCategory.Currency"/>, so a numeric token in a hand-edited file
    /// would silently become a real category and the raw text would stop matching
    /// <see cref="CategoryRef.Known"/> — breaking the record's own invariant. Requiring
    /// <c>Known.ToString() == Raw</c> rejects exactly those cases and keeps the token as written.
    /// </remarks>
    public static CategoryRef ParseCategory(string? raw)
    {
        var text = raw ?? string.Empty;
        return Enum.TryParse<ExchangeCategory>(text, ignoreCase: false, out var known)
            && string.Equals(known.ToString(), text, StringComparison.Ordinal)
                ? new CategoryRef(text, known)
                : new CategoryRef(text, null);
    }

    /// <summary>Formats an instant for the shutdown flush-failure trace file (UTC, ISO 8601).</summary>
    public static string FormatTraceInstant(DateTimeOffset at)
        => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[A-Za-z]{4})?(-[A-Za-z]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagPattern();
}
