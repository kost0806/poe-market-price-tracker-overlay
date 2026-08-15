namespace PoeOverlay.Core.Localization;

/// <summary>
/// The raw-template half of localization (S2 10.3 D-L4 / S4 5.1).
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="ILocalizer"/> because <c>Pricing</c> has to <em>inspect</em> a template
/// before formatting it. Going through <c>Ui(key)</c> with no arguments would format
/// <c>"{0}c"</c> against an empty argument array and throw <see cref="FormatException"/>
/// <em>inside</em> localization, where Pricing's three nets cannot see it — and Pricing must never
/// throw (S2 1.5). <see cref="TryGetTemplate"/> does no formatting, so it has nothing to throw.
/// </para>
/// </remarks>
public interface ITemplateSource
{
    /// <summary>
    /// Resolves <paramref name="key"/> to its raw, unformatted template.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the fallback chain found a non-blank value at levels ① to ③;
    /// <see langword="false"/> when the chain would have reached level ⑤ (the key itself), which is
    /// the caller's signal to use its compile-time constant instead.
    /// </returns>
    bool TryGetTemplate(string key, out string template);
}
