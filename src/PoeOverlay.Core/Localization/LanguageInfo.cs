namespace PoeOverlay.Core.Localization;

/// <summary>
/// A discovered language and the name it calls itself (S4 5.1).
/// </summary>
/// <remarks>
/// <see cref="DisplayName"/> comes from the dictionary's own <c>ui.language.selfName</c>, falling
/// back to <see cref="Tag"/>. <c>CultureInfo</c> is deliberately not consulted — a discovered tag
/// is not guaranteed to be a culture the CLR knows (S2 3.2).
/// </remarks>
public sealed record LanguageInfo(string Tag, string DisplayName);
