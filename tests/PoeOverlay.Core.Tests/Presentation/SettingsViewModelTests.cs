using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Store;
using PoeOverlay.Core.Tests.TestSupport;
using Xunit;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// S3 5, 7.4, 7.6 — banner assembly order, the debounce, the pass-through toggle and the commands
/// that go out through delegates rather than through a sixth face on the Store.
/// </summary>
public sealed class SettingsViewModelTests
{
    private static readonly DateTimeOffset Now = SnapshotBuilder.Now;

    [Fact]
    public void SettingsWindow_ListsEveryActiveCondition_NotJustTheMostUrgent()
    {
        using var fixture = new Fixture();

        fixture.Vm.Refresh(
            SnapshotBuilder.WithConditions(
                (AppConditionKind.SettingsCorrupt, null),
                (AppConditionKind.LoggingUnavailable, @"C:\logs\poe.log"),
                (AppConditionKind.ViewModelRefreshFailing, "OverlayViewModel")),
            Now);

        // Its banner area scrolls, so the overlay's single-slot priority does not apply (S3 5.5).
        var kinds = fixture.Vm.Banners.Select(b => b.Kind).ToArray();
        Assert.Contains(AppConditionKind.SettingsCorrupt, kinds);
        Assert.Contains(AppConditionKind.LoggingUnavailable, kinds);
        Assert.Contains(AppConditionKind.ViewModelRefreshFailing, kinds);
    }

    [Fact]
    public void TheResolvedLeagueIsPublished_BecauseTheLeagueBoxHoldsAnOverrideAndIsUsuallyEmpty()
    {
        // settings.league is null whenever the league is resolved from the list, so the control the
        // user looks at is blank in the ordinary case. Without this the window gave no answer to
        // "which league am I looking at" at all.
        using var fixture = new Fixture();

        Assert.Equal(string.Empty, fixture.Vm.ActiveLeague);

        fixture.Vm.Refresh(
            SnapshotBuilder.Empty() with
            {
                DataLeague = SnapshotBuilder.League,
                LeagueResolution = new LeagueResolution(
                    LeagueResolutionState.Resolved, SnapshotBuilder.League, null),
            },
            Now);

        Assert.Equal(SnapshotBuilder.League, fixture.Vm.ActiveLeague);
    }

    [Fact]
    public void LoggingUnavailable_CarriesThePath_SoTheOpenLogButtonIsNotAMystery()
    {
        using var fixture = new Fixture();

        fixture.Vm.Refresh(
            SnapshotBuilder.WithConditions((AppConditionKind.LoggingUnavailable, @"C:\logs\poe.log")),
            Now);

        var banner = fixture.Vm.Banners.Single(b => b.Kind == AppConditionKind.LoggingUnavailable);
        Assert.Equal(@"log file unavailable — path: C:\logs\poe.log", banner.Text);
    }

    [Fact]
    public void BannersArePublished_EvenWhenTheRestOfRefreshThrows()
    {
        using var fixture = new Fixture();
        fixture.Search.Throw = true;
        fixture.Vm.SearchQuery = "divine";

        Assert.Throws<InvalidOperationException>(() => fixture.Vm.Refresh(
            SnapshotBuilder.WithConditions((AppConditionKind.SettingsCorrupt, null)),
            Now));

        // The banner section is a separate, earlier block precisely because the conditions it
        // reports make the rest of Refresh more likely to misbehave, not less (S3 7.6 M5).
        Assert.Contains(fixture.Vm.Banners, b => b.Kind == AppConditionKind.SettingsCorrupt);
    }

    [Fact]
    public void SearchIsDebounced_AndRunsOnce_AfterTheWindowElapses()
    {
        using var fixture = new Fixture();

        fixture.Vm.SearchQuery = "div";
        fixture.Vm.SearchQuery = "divi";
        fixture.Vm.SearchQuery = "divine";

        Assert.Equal(0, fixture.Search.Calls);

        fixture.Time.Advance(SettingsViewModel.SearchDebounce);
        fixture.Dispatcher.Drain();

        Assert.Equal(1, fixture.Search.Calls);
        Assert.Equal(SearchOutcome.Found, fixture.Vm.SearchOutcome);
        Assert.Single(fixture.Vm.SearchResults);
    }

    [Fact]
    public void TheDebounceCallback_TouchesViewStateOnlyThroughTheDispatcher()
    {
        using var fixture = new Fixture();
        fixture.Vm.SearchQuery = "divine";

        fixture.Time.Advance(SettingsViewModel.SearchDebounce);

        // TimeProvider callbacks arrive on a pool thread; the corrections in S3 4.6.1 M3 apply
        // here for the same reason. Nothing has been recomputed until the post runs.
        Assert.Equal(0, fixture.Search.Calls);

        fixture.Dispatcher.Drain();
        Assert.Equal(1, fixture.Search.Calls);
    }

    [Fact]
    public void MoveModeToggle_IsAPassThrough_AndKeepsTheGeometryCommandsDisabled()
    {
        using var fixture = new Fixture();

        Assert.True(fixture.Vm.ResetPlacementCommand.CanExecute(null));

        fixture.Vm.IsMoveModeActive = true;

        Assert.True(fixture.MoveMode.IsActive);
        Assert.False(fixture.Vm.ResetPlacementCommand.CanExecute(null));
        Assert.False(fixture.Vm.RevertHeightCommand.CanExecute(null));

        fixture.Vm.IsMoveModeActive = false;

        Assert.Equal(MoveModeExitReason.SettingsToggleOff, fixture.MoveMode.LastExitReason);
        Assert.True(fixture.Vm.RevertHeightCommand.CanExecute(null));
    }

    [Fact]
    public void GeometryCommands_GoThroughThePort()
    {
        using var fixture = new Fixture();

        fixture.Vm.ResetPlacementCommand.Execute(null);
        fixture.Vm.RevertHeightCommand.Execute(null);

        // D19's single-writer invariant: the settings window never writes window geometry itself.
        Assert.Equal(1, fixture.Geometry.ResetCount);
        Assert.Equal(1, fixture.Geometry.RevertCount);
    }

    [Fact]
    public void AcknowledgeCorruption_IsOfferedForCorruptAndForNothingElse()
    {
        using var fixture = new Fixture();
        fixture.Settings.BlockReason = WriteBlockReason.Unreadable;
        fixture.Vm.Refresh(SnapshotBuilder.Empty(), Now);

        // Acknowledging Unreadable would overwrite the very file that blocking writes protects
        // (D-SE2).
        Assert.False(fixture.Vm.AcknowledgeCorruptionCommand.CanExecute(null));
        Assert.True(fixture.Vm.WritesBlocked);

        fixture.Settings.BlockReason = WriteBlockReason.Corrupt;
        fixture.Vm.Refresh(SnapshotBuilder.Empty(), Now);
        Assert.True(fixture.Vm.AcknowledgeCorruptionCommand.CanExecute(null));

        fixture.Vm.AcknowledgeCorruptionCommand.Execute(null);
        Assert.Equal(WriteBlockReason.None, fixture.Settings.BlockReason);
        Assert.False(fixture.Vm.WritesBlocked);
    }

    [Fact]
    public void FirstRunBanner_IsDismissedOnceAndPersisted()
    {
        using var fixture = new Fixture();

        Assert.True(fixture.Vm.ShowFirstRunBanner);

        fixture.Vm.DismissFirstRunBannerCommand.Execute(null);

        // FR-08-6: the guidance is once only, and "once" has to survive a restart.
        Assert.True(fixture.Settings.Current.FirstRunAcknowledged);
        Assert.False(fixture.Vm.ShowFirstRunBanner);

        fixture.Vm.DismissFirstRunBannerCommand.Execute(null);
        Assert.Equal(1, fixture.Settings.UpdateCount);
    }

    [Fact]
    public async Task RemoveFromWatchlist_EditsTheStoredList()
    {
        using var fixture = new Fixture();
        await fixture.Vm.AddToWatchlistCommand.ExecuteAsync(
            new SearchHit(new ItemId("divine"), "Divine Orb", ExchangeCategory.Currency, SearchSource.RoundCommitted, 200m, Now));

        Assert.Single(fixture.Settings.Current.Watchlist);

        fixture.Vm.RemoveFromWatchlistCommand.Execute(new ItemId("divine"));

        Assert.Empty(fixture.Settings.Current.Watchlist);
    }

    [Fact]
    public async Task RetryNow_RefetchesEveryFailingCategory_IncludingOneStillInCooldown()
    {
        using var fixture = new Fixture();

        fixture.Vm.Refresh(
            SnapshotBuilder.Empty() with
            {
                DataLeague = SnapshotBuilder.League,
                DataEpoch = 4,
                CategoryStatuses = new Dictionary<ExchangeCategory, CategoryStatus>
                {
                    // Declared first, and still expected second: the retry order is the category
                    // order, not whatever order the map happens to enumerate in.
                    [ExchangeCategory.Scarab] = new(
                        ExchangeCategory.Scarab, 3, Now.AddMinutes(-2), null, Now.AddMinutes(8), null, 0, null, false),
                    [ExchangeCategory.Currency] = new(
                        ExchangeCategory.Currency, 1, Now.AddMinutes(-1), null, null, null, 0, null, false),
                    [ExchangeCategory.Fragment] = new(
                        ExchangeCategory.Fragment, 0, Now.AddMinutes(-1), Now.AddMinutes(-1), null, null, 0, null, false),
                },
            },
            Now);

        await fixture.Vm.RetryNowCommand.ExecuteAsync(null);

        // Scarab is the whole point of the command: its cooldown runs another eight minutes, and
        // that cooldown is Polling's gate on Polling's round — a user-initiated fetch never enters
        // the round, so it is not held by it (S3 5.5). Fragment has no failures and is left alone.
        Assert.Equal(
            new[] { ExchangeCategory.Currency, ExchangeCategory.Scarab },
            fixture.Market.Fetches.Select(f => f.Category));
        Assert.All(fixture.Market.Fetches, f => Assert.Equal(SnapshotBuilder.League, f.League));
        Assert.All(fixture.Market.Fetches, f => Assert.Equal(RequestPriority.UserInitiated, f.Priority));

        // And the results land through the sink the settings window already owns — no channel to
        // Polling is opened, because D-SH2 forbids one and this command never needed one.
        Assert.Equal(
            new[] { ExchangeCategory.Currency, ExchangeCategory.Scarab },
            fixture.Published.Select(p => p.Category));
        Assert.All(fixture.Published, p => Assert.Equal(4, p.Epoch));
        Assert.All(fixture.Published, p => Assert.Equal(SnapshotBuilder.League, p.League));
    }

    [Fact]
    public async Task RetryNow_BeforeAnyLeagueIsSettled_FetchesNothing()
    {
        using var fixture = new Fixture();

        fixture.Vm.Refresh(
            SnapshotBuilder.Empty() with
            {
                CategoryStatuses = new Dictionary<ExchangeCategory, CategoryStatus>
                {
                    [ExchangeCategory.Currency] = new(
                        ExchangeCategory.Currency, 2, Now.AddMinutes(-1), null, null, null, 0, null, false),
                },
            },
            Now);

        await fixture.Vm.RetryNowCommand.ExecuteAsync(null);

        // No settled league means no world to tag the result with — the listing slot would be
        // uncommittable (INV-1), so the fetch is not issued at all.
        Assert.Empty(fixture.Market.Fetches);
        Assert.Empty(fixture.Published);
    }

    [Fact]
    public async Task RetryTrayRegistration_ReportsThroughTheInjectedDelegate()
    {
        using var fixture = new Fixture();

        await fixture.Vm.RetryTrayRegistrationCommand.ExecuteAsync(null);

        // D-SH12: the retry lives in the Shell, and the view model only asks for it.
        Assert.Equal(1, fixture.TrayRetries);
    }

    [Fact]
    public void Dispose_UnsubscribesFromEveryChannel()
    {
        var fixture = new Fixture();
        fixture.Vm.Dispose();

        fixture.MoveMode.EnterMoveMode();
        fixture.Settings.Update(fixture.Settings.Current with { FirstRunAcknowledged = true });

        // Step 5 of S3 5.3: the window's view model stops listening when the window closes; the
        // other two are singletons and are not touched here.
        Assert.True(fixture.Vm.ShowFirstRunBanner);
    }

    [Fact]
    public void SettingsUpdate_FromInsideAFanoutPass_IsRefused()
    {
        using var fixture = new Fixture();
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        using var fanout = new Core.Presentation.Fanout.SnapshotFanout(
            store,
            dispatcher,
            ticker,
            store,
            store,
            fixture.Time,
            new RecordingLogger<Core.Presentation.Fanout.SnapshotFanout>());

        fanout.Attach(new CommandingRefreshable(() =>
            fixture.Vm.DismissFirstRunBannerCommand.Execute(null)));

        // Debug.Assert routes through the trace listeners, and the default one ends the process.
        var saved = System.Diagnostics.Trace.Listeners.Cast<System.Diagnostics.TraceListener>().ToArray();
        System.Diagnostics.Trace.Listeners.Clear();
        try
        {
            store.Publish();
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.AddRange(saved);
        }

        // Settings.Update is the one call D-PS4 forbids outright: it is not a reporting path, so
        // there is no version of it that can be deferred to the end of the pass (S3 8.4).
        Assert.Equal(0, fixture.Settings.UpdateCount);
        Assert.False(fixture.Settings.Current.FirstRunAcknowledged);
    }

    private sealed class CommandingRefreshable(Action onRefresh) : Core.Presentation.Fanout.IRefreshable
    {
        public void Refresh(MarketSnapshot snapshot, DateTimeOffset now) => onRefresh();
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Time = new FakeTimeProvider(Now);
            Settings = new FakeSettingsSource();
            MoveMode = new FakeOverlayModeService();
            Geometry = new FakeOverlayGeometryService();
            Dispatcher = new QueueingUiDispatcher();
            Search = new FakeSearchSource();

            Vm = new SettingsViewModel(
                Search,
                Market,
                Settings,
                new FakeLocalizer(),
                MoveMode,
                Geometry,
                Dispatcher,
                new RecentErrorRing(),
                Time,
                CancellationToken.None,
                (league, epoch, category, snapshot) => Published.Add((league, epoch, category, snapshot)),
                _ => { TrayRetries++; return Task.FromResult(true); },
                _ => LeagueLists++,
                () => LogFolderOpens++,
                new RecordingLogger<SettingsViewModel>());
        }

        public SettingsViewModel Vm { get; }

        public FakeTimeProvider Time { get; }

        public FakeSettingsSource Settings { get; }

        public FakeOverlayModeService MoveMode { get; }

        public FakeOverlayGeometryService Geometry { get; }

        public QueueingUiDispatcher Dispatcher { get; }

        public FakeSearchSource Search { get; }

        /// <summary>Everything that reached the <see cref="FetchedListingSink"/>, in order.</summary>
        public List<(string League, int Epoch, ExchangeCategory Category, CategorySnapshot Snapshot)> Published { get; } = [];

        public int TrayRetries { get; private set; }

        public int LeagueLists { get; private set; }

        public int LogFolderOpens { get; private set; }

        public FakeMarketClient Market { get; } = new();

        public void Dispose() => Vm.Dispose();
    }

    private sealed class FakeSearchSource : ISearchSource
    {
        public int Calls { get; private set; }

        public bool Throw { get; set; }

        public SearchResult Search(string query, SearchOptions options)
        {
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException("the search index is broken");
            }

            return new SearchResult(
                [new SearchHit(new ItemId("divine"), "Divine Orb", ExchangeCategory.Currency, SearchSource.RoundCommitted, 200m, Now)],
                SearchOutcome.Found,
                [],
                false);
        }
    }

    private sealed class FakeMarketClient : IMarketClient
    {
        /// <summary>Every category fetch, in call order, with the priority it carried.</summary>
        public List<(string League, ExchangeCategory Category, RequestPriority Priority)> Fetches { get; } = [];

        public Task<MarketResult<CategorySnapshot>> FetchCategoryAsync(
            string league,
            ExchangeCategory category,
            RequestPriority priority,
            CancellationToken ct)
        {
            Fetches.Add((league, category, priority));
            return Task.FromResult<MarketResult<CategorySnapshot>>(
                new MarketResult<CategorySnapshot>.Ok(
                    SnapshotBuilder.Category(category, Now, [SnapshotBuilder.Price("divine", 200m)])));
        }

        public Task<MarketResult<LeagueList>> FetchLeaguesAsync(RequestPriority priority, CancellationToken ct)
            => Task.FromResult<MarketResult<LeagueList>>(
                new MarketResult<LeagueList>.Ok(
                    new LeagueList([new LeagueEntry("Allflame", "Allflame")], Now, LeagueListStatus.Ok, null)));
    }
}
