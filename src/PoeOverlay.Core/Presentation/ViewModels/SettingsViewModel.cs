using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Presentation.UiState;
using PoeOverlay.Core.Presentation.ViewModels.Rows;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Store;

namespace PoeOverlay.Core.Presentation.ViewModels;

/// <summary>
/// How a user-initiated category fetch reaches the Store without the view model knowing it exists
/// (S4 11.5 B2).
/// </summary>
public delegate void FetchedListingSink(
    string league,
    int dataEpoch,
    ExchangeCategory category,
    CategorySnapshot snapshot);

/// <summary>
/// The one surface the user operates (HLD 6.0 / S3 5, 7.1 / S4 11.5).
/// </summary>
/// <remarks>
/// <para>
/// The only transient of the three view models: D18-b's argument — a hidden window that keeps
/// recomputing an invisible UI — applies to this window and to no other surface (S3 3.1 B5). It is
/// attached on open and detached and disposed on close (S3 5.3).
/// </para>
/// <para>
/// Everything the Store may not know reaches it through delegates rather than through a sixth face
/// on <c>Store</c>, which S3 3.1 froze at five (S4 11.5 B2).
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject, IRefreshable, IDisposable
{
    /// <summary>S4 15.8 — the search debounce window.</summary>
    internal static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>S4 15.8 — the search result ceiling.</summary>
    internal const int SearchLimit = 200;

    private readonly ISearchSource _searchSource;
    private readonly IMarketClient _marketClient;
    private readonly ISettingsSource _settingsSource;
    private readonly ILocalizer _localizer;
    private readonly IOverlayModeService _moveMode;
    private readonly IOverlayGeometryService _geometry;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly RecentErrorRing _errorRing;
    private readonly CancellationToken _windowScope;
    private readonly FetchedListingSink _setFetchedListing;
    private readonly Func<CancellationToken, Task<bool>> _retryTrayRegistration;
    private readonly Action<LeagueList> _publishLeagueList;
    private readonly Action _openLogFolder;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ITimer _searchDebounce;

    private string _dataLeague = string.Empty;
    private int _dataEpoch;
    private IReadOnlyList<ExchangeCategory> _failingCategories = [];
    private bool _disposed;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SearchRowViewModel> _searchResults = [];

    [ObservableProperty]
    private SearchOutcome _searchOutcome = SearchOutcome.CacheEmpty;

    /// <summary>
    /// The search outcome as a sentence rather than an enum member (S3 5.4.3).
    /// </summary>
    /// <remarks>
    /// Three XAML <c>DataTrigger</c>s held these strings before, which is one of the places the
    /// window's English was unreachable by any dictionary. Composing here also keeps
    /// <c>CacheEmpty</c>'s distinct meaning — "nothing fetched yet", not "not in the cache" —
    /// stated in one place rather than in a style.
    /// </remarks>
    [ObservableProperty]
    private string _searchStatusText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<CategoryRowViewModel> _unfetchedCategories = [];

    [ObservableProperty]
    private IReadOnlyList<WatchlistRowViewModel> _watchlist = [];

    [ObservableProperty]
    private IReadOnlyList<LeagueEntry> _leagues = [];

    [ObservableProperty]
    private LeagueListStatus _leaguesStatus = LeagueListStatus.Failed;

    /// <summary>The league-list status as a sentence (S3 5.4.3 E13).</summary>
    [ObservableProperty]
    private string _leagueStatusText = string.Empty;

    /// <summary>
    /// Every fixed string the window draws (S3 5.4.4 D-SH23).
    /// </summary>
    /// <remarks>
    /// One bundle, swapped whole on a language change, so the switch costs one notification rather
    /// than thirty.
    /// </remarks>
    [ObservableProperty]
    private SettingsStrings _strings;

    /// <summary>
    /// The league the data actually carries, which is not the league the user typed.
    /// </summary>
    /// <remarks>
    /// <c>settings.league</c> is null whenever the league is being resolved from the list (S4 10.2),
    /// so the league control on screen is legitimately empty in the ordinary case — and a user
    /// looking at an empty box next to healthy prices cannot tell "resolved for me" from "not
    /// working". This is that answer, and it is read-only: writing it would be a second route into
    /// <c>settings.league</c>.
    /// </remarks>
    [ObservableProperty]
    private string _activeLeague = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<BannerViewModel> _banners = [];

    [ObservableProperty]
    private IReadOnlyList<LogEntry> _recentErrors = [];

    [ObservableProperty]
    private bool _writesBlocked;

    [ObservableProperty]
    private bool _showFirstRunBanner;

    /// <summary>Wires the window-scoped view model.</summary>
    /// <remarks>
    /// Three parameters are additions to S4 11.5, each because the declared surface had no route to
    /// the thing the declared command has to do: <paramref name="uiDispatcher"/> (the debounce timer
    /// fires on a pool thread and its results are bound properties), <paramref name="publishLeagueList"/>
    /// (<c>ReloadLeaguesCommand</c> had nowhere to put the list it fetched) and
    /// <paramref name="openLogFolder"/> (opening a folder is a Shell act). All three follow the
    /// delegate pattern S4 11.5 B2 established for exactly this situation. There is deliberately no
    /// fourth: <c>RetryNowCommand</c> needs nothing new (see <see cref="RetryNowAsync"/>).
    /// </remarks>
    public SettingsViewModel(
        ISearchSource searchSource,
        IMarketClient marketClient,
        ISettingsSource settingsSource,
        ILocalizer localizer,
        IOverlayModeService moveMode,
        IOverlayGeometryService geometry,
        IUiDispatcher uiDispatcher,
        RecentErrorRing errorRing,
        TimeProvider timeProvider,
        CancellationToken windowScopeToken,
        FetchedListingSink setFetchedListing,
        Func<CancellationToken, Task<bool>> retryTrayRegistration,
        Action<LeagueList> publishLeagueList,
        Action openLogFolder,
        ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(searchSource);
        ArgumentNullException.ThrowIfNull(marketClient);
        ArgumentNullException.ThrowIfNull(settingsSource);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(moveMode);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(errorRing);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(setFetchedListing);
        ArgumentNullException.ThrowIfNull(retryTrayRegistration);
        ArgumentNullException.ThrowIfNull(publishLeagueList);
        ArgumentNullException.ThrowIfNull(openLogFolder);
        ArgumentNullException.ThrowIfNull(logger);

        _searchSource = searchSource;
        _marketClient = marketClient;
        _settingsSource = settingsSource;
        _localizer = localizer;
        _strings = new SettingsStrings(localizer);
        _moveMode = moveMode;
        _geometry = geometry;
        _uiDispatcher = uiDispatcher;
        _errorRing = errorRing;
        _windowScope = windowScopeToken;
        _setFetchedListing = setFetchedListing;
        _retryTrayRegistration = retryTrayRegistration;
        _publishLeagueList = publishLeagueList;
        _openLogFolder = openLogFolder;
        _logger = logger;

        // A dedicated timer rather than the 30 s ticker: sharing a timer between two purposes lets
        // a change to one period silently move the other's threshold (S3 7.4 D-PS6).
        _searchDebounce = timeProvider.CreateTimer(
            _ => OnSearchDebounceElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        AddToWatchlistCommand = new AsyncRelayCommand<SearchRowViewModel?>(AddToWatchlistAsync);
        RemoveFromWatchlistCommand = new RelayCommand<ItemId>(RemoveFromWatchlist);
        FetchCategoryCommand = new AsyncRelayCommand<ExchangeCategory>(FetchCategoryAsync);
        ReloadLeaguesCommand = new AsyncRelayCommand(ReloadLeaguesAsync);
        RetryNowCommand = new AsyncRelayCommand(RetryNowAsync);
        AcknowledgeCorruptionCommand = new RelayCommand(AcknowledgeCorruption, () => _settingsSource.BlockReason == WriteBlockReason.Corrupt);
        ResetPlacementCommand = new RelayCommand(() => _geometry.ResetPlacement(), () => !_moveMode.IsActive);
        RevertHeightCommand = new RelayCommand(() => _geometry.RevertHeightToAuto(), () => !_moveMode.IsActive);
        DismissFirstRunBannerCommand = new RelayCommand(DismissFirstRunBanner);
        OpenLogFolderCommand = new RelayCommand(() => _openLogFolder());
        RetryTrayRegistrationCommand = new AsyncRelayCommand(RetryTrayRegistrationAsync);

        _moveMode.StateChanged += OnMoveModeChanged;
        _settingsSource.Changed += OnSettingsChanged;

        // D-PS5 (S3 11). Two of the three view models subscribed and this one did not; the 30 s
        // pass re-ran the search and so the result rows followed a language change by accident,
        // while the labels had no source at all to recompute. This is the only one of the three
        // that ends — Dispose detaches it.
        _localizer.LanguageChanged += OnLanguageChanged;

        Watchlist = BuildWatchlistRows(_settingsSource.Current.Watchlist);
        WritesBlocked = _settingsSource.BlockReason != WriteBlockReason.None;
        ShowFirstRunBanner = !_settingsSource.Current.FirstRunAcknowledged;
        SearchStatusText = ComposeSearchStatus(SearchOutcome);
        LeagueStatusText = ComposeLeagueStatus(LeaguesStatus);
    }

    /// <summary>Adds the selected hit to the watchlist, fetching its category if the cache lacks it.</summary>
    public IAsyncRelayCommand AddToWatchlistCommand { get; }

    /// <summary>Removes one item.</summary>
    public IRelayCommand<ItemId> RemoveFromWatchlistCommand { get; }

    /// <summary>Fetches one category once, outside the polling round (FR-01-1).</summary>
    public IAsyncRelayCommand FetchCategoryCommand { get; }

    /// <summary>Re-fetches the league list.</summary>
    public IAsyncRelayCommand ReloadLeaguesCommand { get; }

    /// <summary>Retries the failing categories now, ignoring their cooldown (S3 5.5).</summary>
    /// <remarks>
    /// Typed <see cref="IAsyncRelayCommand"/> rather than S4 11.5's <c>IRelayCommand</c> — a
    /// widening, since the async interface derives from it. The declared type was chosen when the
    /// command was a fire-and-forget nudge; the work it actually does is a fetch, and a
    /// <c>void</c> command would swallow the fault and give a test nothing to await.
    /// </remarks>
    public IAsyncRelayCommand RetryNowCommand { get; }

    /// <summary>Clears a <see cref="WriteBlockReason.Corrupt"/> block (D-SE2).</summary>
    public IRelayCommand AcknowledgeCorruptionCommand { get; }

    /// <summary>Returns the overlay to its default placement (HLD D22).</summary>
    public IRelayCommand ResetPlacementCommand { get; }

    /// <summary>Returns the overlay to content-driven height (S3 4.4).</summary>
    public IRelayCommand RevertHeightCommand { get; }

    /// <summary>Marks the first-run guidance as seen (FR-08-6).</summary>
    public IRelayCommand DismissFirstRunBannerCommand { get; }

    /// <summary>Opens the log folder (D12).</summary>
    public IRelayCommand OpenLogFolderCommand { get; }

    /// <summary>Re-attempts tray registration while the pump is running (S3 6.2 D-SH12).</summary>
    public IAsyncRelayCommand RetryTrayRegistrationCommand { get; }

    /// <summary>
    /// Move mode, as a pass-through (HLD D4-b).
    /// </summary>
    /// <remarks>
    /// The view model knows the toggle, never the ordering: capture release, geometry, style bits
    /// are sealed inside the Shell implementation.
    /// </remarks>
    public bool IsMoveModeActive
    {
        get => _moveMode.IsActive;
        set
        {
            if (value == _moveMode.IsActive)
            {
                return;
            }

            if (value)
            {
                _moveMode.EnterMoveMode();
            }
            else
            {
                _moveMode.ExitMoveMode(MoveModeExitReason.SettingsToggleOff);
            }
        }
    }

    /// <summary>
    /// Recomputes display state. Banner assembly runs first, and on its own.
    /// </summary>
    /// <remarks>
    /// If the banner list were computed alongside everything else, an exception in the search
    /// re-formatting would mean the pass never reaches the code that reports what is wrong —
    /// and a condition such as <c>SettingsCorrupt</c> makes the rest of this method more likely to
    /// misbehave, not less. Order is the whole fix (S3 7.6 M5).
    /// </remarks>
    public void Refresh(MarketSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Banners = BannerFactory.Assemble(
            snapshot,
            now,
            _settingsSource.Current.RefreshIntervalMinutes,
            _localizer);

        _dataLeague = snapshot.DataLeague ?? string.Empty;
        _dataEpoch = snapshot.DataEpoch;

        // The resolution's own league first: it is the verdict, while DataLeague is the tag on the
        // data that verdict admitted. They agree except in the window between the two.
        ActiveLeague = snapshot.LeagueResolution.League ?? _dataLeague;

        // The set RetryNowCommand works from. Read here rather than at click time because the
        // command must not hold the Store: the snapshot the pass handed us is the only view of it
        // this view model is allowed (S3 9.1).
        _failingCategories = DerivedConditions.FailingCategories(snapshot.CategoryStatuses);

        RunSearch();

        Watchlist = BuildWatchlistRows(_settingsSource.Current.Watchlist);
        WritesBlocked = _settingsSource.BlockReason != WriteBlockReason.None;
        ShowFirstRunBanner = !_settingsSource.Current.FirstRunAcknowledged;
        RecentErrors = _errorRing.Snapshot();

        Leagues = snapshot.Leagues?.Entries ?? [];
        LeaguesStatus = snapshot.Leagues?.Status ?? LeagueListStatus.Failed;
        LeagueStatusText = ComposeLeagueStatus(LeaguesStatus);

        AcknowledgeCorruptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Detaches from every channel this view model subscribed to (S3 5.3, step 5).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _moveMode.StateChanged -= OnMoveModeChanged;
        _settingsSource.Changed -= OnSettingsChanged;
        _localizer.LanguageChanged -= OnLanguageChanged;
        _searchDebounce.Dispose();
    }

    partial void OnSearchQueryChanged(string value)
        => _searchDebounce.Change(SearchDebounce, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// The debounce callback arrives on a pool thread and must not touch bound state there.
    /// </summary>
    /// <remarks>
    /// The same correction S3 4.6.1 M3 made for the move-mode watchdog: the callback's only act is
    /// to post, and every read and write of view state happens inside the posted delegate.
    /// </remarks>
    private void OnSearchDebounceElapsed()
    {
        if (_disposed || _windowScope.IsCancellationRequested)
        {
            return;
        }

        _uiDispatcher.Post(RunSearch);
    }

    private void RunSearch()
    {
        if (_disposed)
        {
            return;
        }

        var query = SearchQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResults = [];
            SearchOutcome = SearchOutcome.CacheEmpty;
            SearchStatusText = ComposeSearchStatus(SearchOutcome.CacheEmpty);
            UnfetchedCategories = [];
            return;
        }

        // ExtraMatch runs inside the Store's iteration, so it stays pure and cheap; a throw there
        // costs one item, not the search (D-ST6).
        var result = _searchSource.Search(query, new SearchOptions(SearchLimit, MatchesLocalisedName));

        // The same ③④⑤ chain the matching predicate below already runs on. Binding ApiName
        // directly left every unnamed hit as a row with nothing in it but its category (D-DL16),
        // and rebuilding here rather than caching is what makes a language change follow: the
        // LanguageChanged handler calls RunSearch, and so does every pass.
        var rows = new List<SearchRowViewModel>(result.Hits.Count);
        foreach (var hit in result.Hits)
        {
            rows.Add(new SearchRowViewModel(
                hit.Id,
                _localizer.ItemName(hit.Id, hit.ApiName),
                hit.Category,
                CategoryLabels.Label(_localizer, hit.Category)));
        }

        var unfetched = new List<CategoryRowViewModel>(result.UnfetchedCategories.Count);
        foreach (var category in result.UnfetchedCategories)
        {
            unfetched.Add(new CategoryRowViewModel(category, CategoryLabels.Label(_localizer, category)));
        }

        SearchResults = rows;
        SearchOutcome = result.Outcome;
        SearchStatusText = ComposeSearchStatus(result.Outcome);
        UnfetchedCategories = unfetched;
    }

    /// <summary>
    /// The watchlist as rows carrying a name (S3 5.4.3 E14).
    /// </summary>
    /// <remarks>
    /// <c>WatchlistEntry</c> has no API name, so level ④ is skipped and an item the dictionary does
    /// not have falls to its slug — which is what this list drew for every item before.
    /// </remarks>
    private IReadOnlyList<WatchlistRowViewModel> BuildWatchlistRows(IReadOnlyList<WatchlistEntry> entries)
    {
        var rows = new List<WatchlistRowViewModel>(entries.Count);
        foreach (var entry in entries)
        {
            rows.Add(new WatchlistRowViewModel(entry.Id, _localizer.ItemName(entry.Id, apiName: null)));
        }

        return rows;
    }

    private string ComposeSearchStatus(SearchOutcome outcome)
        => _localizer.Ui(outcome switch
        {
            SearchOutcome.Found => SettingsKeys.SearchFound,
            SearchOutcome.NotInCache => SettingsKeys.SearchNotInCache,
            _ => SettingsKeys.SearchCacheEmpty,
        });

    private string ComposeLeagueStatus(LeagueListStatus status)
        => _localizer.Ui(status switch
        {
            LeagueListStatus.Ok => SettingsKeys.LeagueStatusOk,
            LeagueListStatus.Suspicious => SettingsKeys.LeagueStatusSuspicious,
            _ => SettingsKeys.LeagueStatusFailed,
        });

    private bool MatchesLocalisedName(ItemId id, string? apiName)
        => _localizer.ItemName(id, apiName).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);

    private async Task AddToWatchlistAsync(SearchRowViewModel? hit)
    {
        if (hit is null)
        {
            return;
        }

        var current = _settingsSource.Current;
        foreach (var existing in current.Watchlist)
        {
            if (existing.Id == hit.Id)
            {
                return;
            }
        }

        var entry = new WatchlistEntry(hit.Id, new CategoryRef(hit.Category.ToString(), hit.Category), null);
        var next = new List<WatchlistEntry>(current.Watchlist) { entry };
        Commit(current with { Watchlist = new EquatableArray<WatchlistEntry>(next) });

        if (!HasData(hit.Category))
        {
            await FetchCategoryAsync(hit.Category);
        }
    }

    private void RemoveFromWatchlist(ItemId id)
    {
        var current = _settingsSource.Current;
        var next = new List<WatchlistEntry>(current.Watchlist.Count);
        var removed = false;

        foreach (var entry in current.Watchlist)
        {
            if (!removed && entry.Id == id)
            {
                removed = true;
                continue;
            }

            next.Add(entry);
        }

        if (removed)
        {
            Commit(current with { Watchlist = new EquatableArray<WatchlistEntry>(next) });
        }
    }

    private async Task FetchCategoryAsync(ExchangeCategory category)
    {
        if (string.IsNullOrEmpty(_dataLeague))
        {
            // No settled league means no world to tag the data with; the listing slot would be
            // uncommittable (INV-1).
            return;
        }

        var result = await _marketClient.FetchCategoryAsync(
            _dataLeague,
            category,
            RequestPriority.UserInitiated,
            _windowScope);

        if (result is MarketResult<CategorySnapshot>.Ok ok)
        {
            _setFetchedListing(_dataLeague, _dataEpoch, category, ok.Value);
            return;
        }

        Log(LogLevel.Warning, "UserFetchFailed", $"a user-initiated fetch of {category} failed");
    }

    private async Task ReloadLeaguesAsync()
    {
        var result = await _marketClient.FetchLeaguesAsync(RequestPriority.UserInitiated, _windowScope);
        if (result is MarketResult<LeagueList>.Ok ok)
        {
            // Market renders the verdict; acting on a Suspicious list is Polling's decision (D6),
            // so the list is published unchanged.
            _publishLeagueList(ok.Value);
            Leagues = ok.Value.Entries;
            LeaguesStatus = ok.Value.Status;
            return;
        }

        LeaguesStatus = LeagueListStatus.Failed;
        Log(LogLevel.Warning, "LeagueReloadFailed", "a user-initiated league list reload failed");
    }

    /// <summary>
    /// The "retry now" of S3 5.5 and HLD 6.4's <c>FetchFailed</c> row: every currently failing
    /// category, re-fetched at once, cooldown or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This needs no channel to <c>Polling</c>, and D-SH2 forbids one. The two statements only look
    /// contradictory: D-SH2 is about restarting a dead loop, which only a process restart does,
    /// while "retry now" is category-level and lives entirely on the route
    /// <c>FetchCategoryCommand</c> already uses — <see cref="IMarketClient"/> out, the
    /// <see cref="FetchedListingSink"/> back in. The cooldown is <c>Polling</c>'s own gate on its
    /// own round; a fetch that never enters the round cannot be held by it, so "ignoring the
    /// cooldown" costs nothing here and is not a flag anyone has to pass.
    /// </para>
    /// <para>
    /// Sequential, not <c>Task.WhenAll</c>: eighteen categories fired together would be a burst the
    /// user asked for by pressing one button, and <c>Market</c>'s own limiter would serialise them
    /// anyway — with the failures arriving in an order nobody chose.
    /// </para>
    /// </remarks>
    private async Task RetryNowAsync()
    {
        foreach (var category in _failingCategories)
        {
            await FetchCategoryAsync(category);
        }
    }

    private void AcknowledgeCorruption()
    {
        _settingsSource.Acknowledge();
        WritesBlocked = _settingsSource.BlockReason != WriteBlockReason.None;
        AcknowledgeCorruptionCommand.NotifyCanExecuteChanged();
    }

    private void DismissFirstRunBanner()
    {
        var current = _settingsSource.Current;
        if (current.FirstRunAcknowledged)
        {
            return;
        }

        Commit(current with { FirstRunAcknowledged = true });
        ShowFirstRunBanner = false;
    }

    private async Task RetryTrayRegistrationAsync()
    {
        var succeeded = await _retryTrayRegistration(_windowScope);
        Log(
            succeeded ? LogLevel.Information : LogLevel.Warning,
            "TrayReregistration",
            succeeded ? "tray registration succeeded on retry" : "tray registration failed on retry");
    }

    /// <summary>
    /// The single place this view model writes settings.
    /// </summary>
    /// <remarks>
    /// <c>Settings.Update</c> from inside a fan-out pass is the one thing D-PS4 forbids outright:
    /// unlike the two diagnostic sinks it is not a reporting path, so there is no version of it
    /// that can be deferred to the end of the pass. Every write goes through here so the guard has
    /// exactly one place to watch (S3 8.4).
    /// </remarks>
    private void Commit(AppSettings next)
    {
        if (!UiPassGuard.CheckNotInPass(_logger, "ISettingsSource.Update"))
        {
            return;
        }

        _settingsSource.Update(next);
    }

    private bool HasData(ExchangeCategory category)
    {
        foreach (var unfetched in UnfetchedCategories)
        {
            if (unfetched.Category == category)
            {
                return false;
            }
        }

        return true;
    }

    private void OnMoveModeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsMoveModeActive));
        ResetPlacementCommand.NotifyCanExecuteChanged();
        RevertHeightCommand.NotifyCanExecuteChanged();
    }

    private void OnSettingsChanged(AppSettings oldSettings, AppSettings newSettings)
    {
        Watchlist = BuildWatchlistRows(newSettings.Watchlist);
        WritesBlocked = _settingsSource.BlockReason != WriteBlockReason.None;
        ShowFirstRunBanner = !newSettings.FirstRunAcknowledged;
    }

    /// <summary>
    /// Re-resolves every string this window shows, outside a fan-out pass (D-PS5, S3 11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Language change is Localization's signal, not the Store's, so it arrives on its own channel.
    /// Unlike the overlay's handler this one cannot leave the rest to the next pass: the labels are
    /// not derived from a snapshot at all, and the item names in two lists depend on the language
    /// through the fallback chain (S2 3.4).
    /// </para>
    /// <para>
    /// No file I/O happens here — D-L1 loaded every dictionary at startup precisely so that this
    /// path, which runs on the UI thread, is nothing but frozen-dictionary lookups.
    /// </para>
    /// </remarks>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Strings = new SettingsStrings(_localizer);
        LeagueStatusText = ComposeLeagueStatus(LeaguesStatus);
        Watchlist = BuildWatchlistRows(_settingsSource.Current.Watchlist);

        // Rebuilds the result rows, their category labels and the status sentence in one call.
        RunSearch();
    }

    private void Log(LogLevel level, string code, string message)
        => _logger.Log(level, new EventId(0, code), message, null, static (state, _) => state);
}
