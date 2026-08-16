using System.ComponentModel;
using System.Runtime.CompilerServices;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Settings;

/// <summary>
/// The five immediately-applied settings the settings window edits directly.
/// </summary>
/// <remarks>
/// S3 5.4 requires league, interval, language, display currency and opacity controls, and S4 11.5's
/// <c>SettingsViewModel</c> surface declares none of them — it has the watchlist, the search and the
/// banners, but no editable scalar settings at all. Rather than widen a frozen Presentation
/// signature, the Shell owns this adapter. Doing so also leaves the Shell the sole writer of every
/// <c>window.*</c> key including <c>opacity</c>, which is a simplification of S3 4.3's split, not a
/// violation of D19 — the single-writer invariant is what D19 asks for.
/// </remarks>
public sealed class SettingsEditor : INotifyPropertyChanged
{
    private readonly ISettingsSource _settings;
    private readonly ILocalizer _localizer;

    /// <summary>Wires the adapter.</summary>
    /// <param name="settings">The value store.</param>
    /// <param name="localizer">Applied immediately on a language change (D10).</param>
    internal SettingsEditor(ISettingsSource settings, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);

        _settings = settings;
        _localizer = localizer;
        _settings.Changed += OnSettingsChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Every discovered language.</summary>
    public IReadOnlyList<LanguageInfo> Languages => _localizer.Languages;

    /// <summary>The three display-currency choices (FR-04-3).</summary>
    public IReadOnlyList<DisplayCurrency> DisplayCurrencies { get; } =
        [DisplayCurrency.Auto, DisplayCurrency.Chaos, DisplayCurrency.Divine];

    /// <summary>The user-entered league, or null for "resolve from the league list".</summary>
    public string? League
    {
        get => _settings.Current.League;
        set => Apply(current => current with { League = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    /// <summary>Poll period in minutes; the store clamps it to [5, 60].</summary>
    public int RefreshIntervalMinutes
    {
        get => _settings.Current.RefreshIntervalMinutes;
        set => Apply(current => current with { RefreshIntervalMinutes = value });
    }

    /// <summary>The selected language tag. Applied to the localizer as well as stored.</summary>
    public string Language
    {
        get => _settings.Current.Language;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, _settings.Current.Language, StringComparison.Ordinal))
            {
                return;
            }

            _localizer.SetLanguage(value);
            Apply(current => current with { Language = value });
        }
    }

    /// <summary>The fallback display currency for entries that do not choose one.</summary>
    public DisplayCurrency DefaultDisplayCurrency
    {
        get => _settings.Current.DefaultDisplayCurrency;
        set => Apply(current => current with { DefaultDisplayCurrency = value });
    }

    /// <summary>
    /// Window-wide opacity (FR-05-5).
    /// </summary>
    /// <remarks>
    /// Unaffected by the <c>AllowsTransparency=false</c> switch: <c>LWA_ALPHA</c> supplies a uniform
    /// window alpha directly (<c>00-shell-measurements.md</c> §8.6). Per-panel translucency is what
    /// was lost, and no requirement asked for it.
    /// </remarks>
    public double Opacity
    {
        get => _settings.Current.Window.Opacity;
        set => Apply(current => current with { Window = current.Window with { Opacity = value } });
    }

    /// <summary>Stops listening. Called when the window closes.</summary>
    internal void Detach() => _settings.Changed -= OnSettingsChanged;

    private void Apply(Func<AppSettings, AppSettings> mutate)
    {
        var current = _settings.Current;
        var next = mutate(current);
        if (!Equals(current, next))
        {
            _settings.Update(next);
        }
    }

    private void OnSettingsChanged(AppSettings oldSettings, AppSettings newSettings)
    {
        Raise(nameof(League));
        Raise(nameof(RefreshIntervalMinutes));
        Raise(nameof(Language));
        Raise(nameof(DefaultDisplayCurrency));
        Raise(nameof(Opacity));
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
