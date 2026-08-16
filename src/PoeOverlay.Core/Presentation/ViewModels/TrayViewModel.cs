using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Presentation.UiState;
using PoeOverlay.Core.Presentation.ViewModels.Rows;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Core.Presentation.ViewModels;

/// <summary>The three icon variants of D21.</summary>
public enum TrayIconVariant
{
    /// <summary>Nothing to report.</summary>
    Normal,

    /// <summary>Degraded, and it may recover without the user.</summary>
    Warning,

    /// <summary>The user has to do something.</summary>
    Error,
}

/// <summary>
/// The tray's state surface (HLD D21 / S3 6.3 / S4 11.6).
/// </summary>
/// <remarks>
/// <para>
/// The threshold logic lives here rather than in <c>TrayIconHost</c> so that it is testable; the
/// host binds <c>Icon</c> and <c>Text</c> and does nothing else. Three variants, not two, because
/// "this will fix itself" and "you have to act" are different messages.
/// </para>
/// <para>
/// The tooltip is assembled worst-first and truncated by the assembler. <c>NotifyIcon.Text</c>
/// throws above its limit, and a throw here becomes an unhandled UI-thread exception under an empty
/// allow-list (D-SH13) — so the length rule is enforced where the string is built, not where it is
/// assigned.
/// </para>
/// </remarks>
public sealed partial class TrayViewModel : ObservableObject, IRefreshable
{
    /// <summary>S4 15.7 — the historical WinForms ceiling for <c>NotifyIcon.Text</c>.</summary>
    internal const int TooltipMaxLength = 63;

    private readonly ILocalizer _localizer;
    private readonly IOverlayModeService _moveMode;
    private readonly ISettingsSource _settings;
    private readonly ILogger<TrayViewModel> _logger;

    [ObservableProperty]
    private TrayIconVariant _iconVariant = TrayIconVariant.Normal;

    [ObservableProperty]
    private string _tooltipText = string.Empty;

    [ObservableProperty]
    private bool _showMoveModeOffMenuItem;

    /// <summary>
    /// Builds the view model. <paramref name="timeProvider"/> is accepted and not stored, and
    /// <paramref name="settings"/> is an addition to S4 11.6 — see <see cref="OverlayViewModel"/>
    /// for both reasons.
    /// </summary>
    public TrayViewModel(
        ILocalizer localizer,
        IOverlayModeService moveMode,
        ISettingsSource settings,
        TimeProvider timeProvider,
        ILogger<TrayViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(moveMode);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _localizer = localizer;
        _moveMode = moveMode;
        _settings = settings;
        _logger = logger;

        _moveMode.StateChanged += OnMoveModeChanged;
        _localizer.LanguageChanged += OnLanguageChanged;

        ShowMoveModeOffMenuItem = _moveMode.IsActive;
        TooltipText = Truncate(_localizer.Ui(UiStateKeys.TrayAppName));
    }

    /// <summary>The banner list this view model last rendered. Test observability for D21.</summary>
    internal IReadOnlyList<BannerViewModel> LastBanners { get; private set; } = [];

    /// <inheritdoc />
    public void Refresh(MarketSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var banners = BannerFactory.Assemble(
            snapshot,
            now,
            _settings.Current.RefreshIntervalMinutes,
            _localizer);

        LastBanners = banners;
        IconVariant = ClassifyIcon(banners);
        TooltipText = ComposeTooltip(banners);
    }

    /// <summary>
    /// Worst severity wins (D21).
    /// </summary>
    /// <remarks>
    /// An <c>Info</c> banner — a pending or inherited rate — is not a reason to change the icon: the
    /// icon would then be non-normal most of the time and stop meaning anything.
    /// </remarks>
    private static TrayIconVariant ClassifyIcon(IReadOnlyList<BannerViewModel> banners)
    {
        var variant = TrayIconVariant.Normal;
        foreach (var banner in banners)
        {
            switch (banner.Severity)
            {
                case BannerSeverity.Error:
                    return TrayIconVariant.Error;
                case BannerSeverity.Warning:
                    variant = TrayIconVariant.Warning;
                    break;
                default:
                    break;
            }
        }

        return variant;
    }

    /// <summary>
    /// Most severe summary first, app name only if it still fits, the remainder as "(+n more)".
    /// </summary>
    /// <remarks>
    /// The order matters because the thing that gets cut has to be the least important thing. An
    /// "app name first" assembly always truncates the summary, which is the half worth reading
    /// (D21).
    /// </remarks>
    private string ComposeTooltip(IReadOnlyList<BannerViewModel> banners)
    {
        if (banners.Count == 0)
        {
            return Truncate(_localizer.Ui(UiStateKeys.TrayAppName));
        }

        var builder = new StringBuilder();
        var shown = 0;

        foreach (var banner in banners)
        {
            var candidate = builder.Length == 0 ? banner.Text : builder + "\n" + banner.Text;
            if (RemainingFits(candidate, banners.Count - shown - 1))
            {
                builder.Clear();
                builder.Append(candidate);
                shown++;
                continue;
            }

            break;
        }

        if (shown == 0)
        {
            // Not even the worst line fits whole; show it cut rather than showing nothing.
            return Truncate(banners[0].Text);
        }

        var hidden = banners.Count - shown;
        if (hidden > 0)
        {
            builder.Append('\n').Append(MoreSuffix(hidden));
            return Truncate(builder.ToString());
        }

        var appName = _localizer.Ui(UiStateKeys.TrayAppName);
        var withName = builder + "\n" + appName;
        return Truncate(withName.Length <= TooltipMaxLength ? withName : builder.ToString());
    }

    private bool RemainingFits(string candidate, int remaining)
    {
        var suffix = remaining > 0 ? "\n" + MoreSuffix(remaining) : string.Empty;
        return candidate.Length + suffix.Length <= TooltipMaxLength;
    }

    private string MoreSuffix(int hidden)
        => UiStateFormat.Ui(
            _localizer,
            UiStateKeys.TrayTooltipMore,
            UiStateTemplates.TrayTooltipMore,
            UiStateFormat.Count(hidden));

    private static string Truncate(string text)
        => text.Length <= TooltipMaxLength ? text : text[..TooltipMaxLength];

    /// <summary>
    /// Move mode is a tooltip line and a menu item, never a fourth icon (S3 4.6.2 D-SH10).
    /// </summary>
    private void OnMoveModeChanged(object? sender, EventArgs e)
    {
        ShowMoveModeOffMenuItem = _moveMode.IsActive;
        _logger.Log(
            LogLevel.Debug,
            new EventId(0, "TrayMoveModeChanged"),
            "tray move-mode affordance updated",
            null,
            static (state, _) => state);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => TooltipText = ComposeTooltip(LastBanners);
}
