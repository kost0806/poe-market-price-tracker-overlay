using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Presentation.UiState;
using PoeOverlay.Core.Presentation.ViewModels.Rows;
using PoeOverlay.Core.Pricing;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Core.Presentation.ViewModels;

/// <summary>
/// The always-on surface: banner, rows, footer (HLD 3.4, 6.0 / S3 7.1 / S4 11.4).
/// </summary>
/// <remarks>
/// <para>
/// Display only — nothing here is clickable, and nothing here writes. It owns no clock: every
/// instant arrives with the pass, so the first and last rows cannot disagree about whether the rate
/// expired (D-PR7).
/// </para>
/// <para>
/// A composition-root singleton, not a transient: the window may close and reopen, and the view
/// model outlives it. D18-b's argument for transient view models — "a hidden window keeps
/// refreshing an invisible UI" — is about the settings window; the overlay is always visible
/// (S3 3.1 B5).
/// </para>
/// </remarks>
public sealed partial class OverlayViewModel : ObservableObject, IRefreshable
{
    private readonly ILocalizer _localizer;
    private readonly ISettingsSource _settings;
    private readonly ILogger<OverlayViewModel> _logger;

    [ObservableProperty]
    private DisplayState _state = DisplayState.Loading;

    [ObservableProperty]
    private IReadOnlyList<PriceRowViewModel> _rows = [];

    [ObservableProperty]
    private IReadOnlyList<BannerViewModel> _banners = [];

    [ObservableProperty]
    private string _footerAttribution = string.Empty;

    [ObservableProperty]
    private string _footerRelativeTime = string.Empty;

    [ObservableProperty]
    private int _failedCategoryCount;

    [ObservableProperty]
    private int _hiddenRowCount;

    [ObservableProperty]
    private string _moreRowsText = string.Empty;

    /// <summary>
    /// Builds the view model. <paramref name="timeProvider"/> is accepted for composition symmetry
    /// and deliberately not stored.
    /// </summary>
    /// <remarks>
    /// Reading the clock inside <c>Refresh</c> is exactly what S3 9.2 forbids — the pass supplies
    /// <c>now</c>. The parameter stays in the signature because S4 11.4 declares it and the Shell's
    /// registration passes it.
    /// <para>
    /// <paramref name="settings"/> is not in S4 11.4's signature and has to be: every staleness
    /// threshold is a function of <c>refreshIntervalMinutes</c>, and the watchlist — the list of
    /// rows this view model exists to draw — lives in settings and nowhere in the snapshot.
    /// </para>
    /// </remarks>
    public OverlayViewModel(
        ILocalizer localizer,
        ISettingsSource settings,
        TimeProvider timeProvider,
        ILogger<OverlayViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _localizer = localizer;
        _settings = settings;
        _logger = logger;

        _localizer.LanguageChanged += OnLanguageChanged;
        FooterAttribution = _localizer.Ui(UiStateKeys.FooterAttribution);
    }

    /// <summary>
    /// Recomputes every displayed value from one snapshot and one instant.
    /// </summary>
    /// <remarks>
    /// Banners first and on their own, for the reason S3 7.6 gives for the settings window: the
    /// list that has to survive is the one describing what is broken, so it must not sit downstream
    /// of the row formatting that might be what is broken.
    /// </remarks>
    public void Refresh(MarketSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var settings = _settings.Current;
        var interval = settings.RefreshIntervalMinutes;

        var all = BannerFactory.Assemble(snapshot, now, interval, _localizer);
        var top = BannerFactory.TopOverlayBanner(all);
        Banners = top is null ? [] : [top];

        State = DerivedConditions.ClassifyDisplayState(snapshot.Heartbeat);
        FailedCategoryCount = DerivedConditions.FailedCategoryCount(snapshot.CategoryStatuses);
        Rows = BuildRows(snapshot, now, settings);

        var latest = DerivedConditions.LatestFetchedAt(snapshot.Categories);
        FooterRelativeTime = latest is { } at
            ? PricingEngine.Relative(at, now, _localizer)
            : string.Empty;

        MoreRowsText = ComposeMoreRowsText(HiddenRowCount, settings.Window.HeightMode);
    }

    /// <summary>Recomposes the clipping marker when the Shell reports a new hidden-row count.</summary>
    /// <remarks>
    /// The count comes from the Shell because it comes from an actual layout pass — D19 forbids
    /// estimating it, and pixels never reach this layer (S2 10.7).
    /// </remarks>
    partial void OnHiddenRowCountChanged(int value)
        => MoreRowsText = ComposeMoreRowsText(value, _settings.Current.Window.HeightMode);

    private string ComposeMoreRowsText(int hidden, HeightMode heightMode)
    {
        if (hidden <= 0)
        {
            return string.Empty;
        }

        // S3 4.4.2: clipping caused by the user's own explicit height says so, because otherwise it
        // reads as a defect rather than as the consequence of a setting they can change.
        return heightMode == HeightMode.Explicit
            ? UiStateFormat.Ui(_localizer, UiStateKeys.MoreRowsExplicit, UiStateTemplates.MoreRowsExplicit, UiStateFormat.Count(hidden))
            : UiStateFormat.Ui(_localizer, UiStateKeys.MoreRows, UiStateTemplates.MoreRows, UiStateFormat.Count(hidden));
    }

    private IReadOnlyList<PriceRowViewModel> BuildRows(
        MarketSnapshot snapshot,
        DateTimeOffset now,
        AppSettings settings)
    {
        var watchlist = settings.Watchlist;
        if (watchlist.Count == 0)
        {
            return [];
        }

        var rateMaxAge = StalenessPolicy.RateMaxAge(settings.RefreshIntervalMinutes);
        var rows = new List<PriceRowViewModel>(watchlist.Count);

        foreach (var entry in watchlist)
        {
            rows.Add(BuildRow(entry, snapshot, now, settings, rateMaxAge));
        }

        return rows;
    }

    private PriceRowViewModel BuildRow(
        WatchlistEntry entry,
        MarketSnapshot snapshot,
        DateTimeOffset now,
        AppSettings settings,
        TimeSpan rateMaxAge)
    {
        var name = _localizer.ItemName(entry.Id, null);

        // An unresolved category token cannot be looked up at all. It is not "loading" — no round
        // will ever fetch it — so it reads as unresolved from the first pass (S2 2.2 D-D1).
        if (entry.Category.Known is not { } category)
        {
            return Placeholder(entry.Id, name, RowKind.ItemUnresolved, now);
        }

        snapshot.Categories.TryGetValue(category, out var categorySnapshot);
        snapshot.CategoryStatuses.TryGetValue(category, out var status);

        if (categorySnapshot is null || !categorySnapshot.Items.TryGetValue(entry.Id, out var price))
        {
            var kind = DerivedConditions.ClassifyRow(
                categorySnapshot is not null,
                status is { ConsecutiveFailures: > 0 },
                categorySnapshot is not null && Contains(categorySnapshot.SkippedIds, entry.Id));

            var at = categorySnapshot?.FetchedAt ?? status?.LastAttemptAt;
            return Placeholder(entry.Id, name, kind, now, at);
        }

        var resolved = PricingEngine.Resolve(
            entry.DisplayCurrency,
            settings.DefaultDisplayCurrency,
            price.MaxVolumeCurrency);

        var display = PricingEngine.Format(
            price,
            snapshot.Rate,
            resolved,
            categorySnapshot.FetchedAt,
            now,
            rateMaxAge,
            _localizer);

        return new PriceRowViewModel(
            entry.Id,
            _localizer.ItemName(entry.Id, price.ApiName),
            display,
            PricingEngine.Relative(categorySnapshot.FetchedAt, now, _localizer),
            display.RateInherited,
            DerivedConditions.IsRowStale(
                categorySnapshot.FetchedAt,
                now,
                settings.RefreshIntervalMinutes),
            RowKind.Normal);
    }

    private PriceRowViewModel Placeholder(
        ItemId id,
        string name,
        RowKind kind,
        DateTimeOffset now,
        DateTimeOffset? at = null)
    {
        var text = kind switch
        {
            RowKind.ItemDropped => UiStateFormat.Ui(_localizer, UiStateKeys.ItemDropped, UiStateTemplates.ItemDroppedRow),
            RowKind.ItemUnresolved => UiStateFormat.Ui(_localizer, UiStateKeys.ItemUnresolved, UiStateTemplates.ItemUnresolvedRow),
            RowKind.FetchFailed => UiStateFormat.Ui(
                _localizer,
                UiStateKeys.FetchFailedRow,
                UiStateTemplates.FetchFailedRow,
                at is { } failedAt ? PricingEngine.Relative(failedAt, now, _localizer) : string.Empty),
            _ => string.Empty,
        };

        return new PriceRowViewModel(
            id,
            name,
            new PriceDisplay(PriceForm.Unavailable, text, at ?? now, false),
            at is { } shownAt ? PricingEngine.Relative(shownAt, now, _localizer) : string.Empty,
            false,
            false,
            kind);
    }

    private static bool Contains(IReadOnlyList<ItemId> ids, ItemId id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Re-resolves the strings this view model holds outside a pass (D-PS5, S3 11).
    /// </summary>
    /// <remarks>
    /// Language change is Localization's signal, not the Store's, so it arrives on its own channel
    /// rather than through the fan-out. Only the attribution line is recomputed here; everything
    /// else is rebuilt by the next pass, which the 30 s tick guarantees.
    /// </remarks>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        FooterAttribution = _localizer.Ui(UiStateKeys.FooterAttribution);
        MoreRowsText = ComposeMoreRowsText(HiddenRowCount, _settings.Current.Window.HeightMode);
        _logger.Log(
            LogLevel.Debug,
            new EventId(0, "OverlayLanguageChanged"),
            "overlay strings re-resolved after a language change",
            null,
            static (state, _) => state);
    }
}
