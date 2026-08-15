using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Localization;

/// <summary>
/// The presentation-facing half of localization (S2 10.3 D-L4 / S4 5.1).
/// </summary>
/// <remarks>
/// Two key spaces share one dictionary file, told apart by prefix: <c>ui.</c> keys are developer
/// defined, everything else is a poe.ninja item slug (S2 3.1). Only the item-name space can fall
/// back to the API name (level ④).
/// </remarks>
public interface ILocalizer : ITemplateSource
{
    /// <summary>Resolves a <c>ui.*</c> key and formats it with <paramref name="args"/>.</summary>
    string Ui(string key, params string[] args);

    /// <summary>
    /// Resolves an item name, falling back to <paramref name="apiName"/> (level ④) and finally to
    /// the slug itself (level ⑤).
    /// </summary>
    string ItemName(ItemId id, string? apiName);

    /// <summary>Every discovered language, plus the embedded floor. Never empty.</summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>The tag currently selected. Never blank.</summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Switches language. UI thread only — D10 recomputes every string on that thread, which is why
    /// D-L1 loads every dictionary at startup so this path touches no file I/O.
    /// </summary>
    void SetLanguage(string tag);

    /// <summary>Raised after <see cref="CurrentLanguage"/> has been published (S2 3.5).</summary>
    event EventHandler? LanguageChanged;
}
