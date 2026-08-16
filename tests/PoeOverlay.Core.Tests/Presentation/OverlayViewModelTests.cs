using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Presentation.ViewModels.Rows;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Tests.TestSupport;
using Xunit;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// S3 5.5 (the overlay banner priority) and S2 10.5 (the four row branches), as the overlay renders
/// them.
/// </summary>
public sealed class OverlayViewModelTests
{
    private static readonly DateTimeOffset Now = SnapshotBuilder.Now;

    [Fact]
    public void OverlayShowsOneBanner_AndItIsTheMostUrgentActiveOne()
    {
        var (vm, _) = Build();
        var snapshot = SnapshotBuilder.WithConditions(
            (AppConditionKind.SettingsWriteFailed, null),
            (AppConditionKind.SettingsCorrupt, null),
            (AppConditionKind.LeagueUnresolved, null));

        vm.Refresh(snapshot, Now);

        // The discriminating property is which condition won, not how many banners there are.
        var banner = Assert.Single(vm.Banners);
        Assert.Equal(AppConditionKind.SettingsCorrupt, banner.Kind);
    }

    [Fact]
    public void TrayUnavailable_OutranksLeagueUnresolved()
    {
        var (vm, _) = Build();

        vm.Refresh(
            SnapshotBuilder.WithConditions(
                (AppConditionKind.LeagueUnresolved, null),
                (AppConditionKind.TrayUnavailable, null)),
            Now);

        // M4: losing the entry point is worse than losing the data, because the banner is close to
        // the only surface left that can say "open the settings window".
        Assert.Equal(AppConditionKind.TrayUnavailable, Assert.Single(vm.Banners).Kind);
    }

    [Fact]
    public void CommitRejected_SitsBetweenLeagueUnresolvedAndSettingsWriteFailed()
    {
        var (vm, _) = Build();

        vm.Refresh(
            SnapshotBuilder.WithConditions(
                (AppConditionKind.CommitRejected, null),
                (AppConditionKind.SettingsWriteFailed, null)),
            Now);
        Assert.Equal(AppConditionKind.CommitRejected, Assert.Single(vm.Banners).Kind);

        vm.Refresh(
            SnapshotBuilder.WithConditions(
                (AppConditionKind.CommitRejected, null),
                (AppConditionKind.LeagueUnresolved, null)),
            Now);
        Assert.Equal(AppConditionKind.LeagueUnresolved, Assert.Single(vm.Banners).Kind);
    }

    [Fact]
    public void ConditionsWithNoOverlaySlot_NeverReachTheOverlay()
    {
        var (vm, _) = Build();

        vm.Refresh(
            SnapshotBuilder.WithConditions(
                (AppConditionKind.ViewModelRefreshFailing, "OverlayViewModel"),
                (AppConditionKind.LoggingUnavailable, @"C:\logs")),
            Now);

        // Reporting "the display is not updating" on the display that is not updating would be a
        // message nobody can read (S3 10.1).
        Assert.Empty(vm.Banners);
    }

    [Fact]
    public void PollingStopped_RendersADifferentLine_PerBranch()
    {
        var (vm, _) = Build();
        var stale = Empty() with
        {
            Heartbeat = new Heartbeat(Now.AddHours(-1), 4, Now.AddHours(-1), RoundOutcome.Completed, false, null, null),
        };

        vm.Refresh(stale, Now);
        var delayed = Assert.Single(vm.Banners);
        Assert.Equal(AppConditionKind.PollingStopped, delayed.Kind);
        Assert.Equal("updates are delayed. last attempt 1h ago", delayed.Text);

        var exited = Empty() with
        {
            Heartbeat = new Heartbeat(Now, 4, Now, RoundOutcome.Completed, true, LoopExitKind.Faulted, Now),
        };

        vm.Refresh(exited, Now);
        var stopped = Assert.Single(vm.Banners);

        // Telling a recovering app to restart, or telling a dead loop to wait, are both lies; the
        // branch is the whole reason S3 2.2 split the text.
        Assert.Equal("updates have stopped. restart the app", stopped.Text);
    }

    [Fact]
    public void Rows_ArePricedWhenTheItemIsInTheSnapshot()
    {
        var (vm, settings) = Build(Watchlist(("divine", ExchangeCategory.Currency)));
        var snapshot = WithCategory(
            SnapshotBuilder.Category(
                ExchangeCategory.Currency,
                Now,
                [SnapshotBuilder.Price("divine", 200m, "Divine Orb")]));

        vm.Refresh(snapshot, Now);

        var row = Assert.Single(vm.Rows);
        Assert.Equal(RowKind.Normal, row.Kind);
        Assert.Equal("Divine Orb", row.DisplayName);
        Assert.False(row.IsStale);
        Assert.Equal("just now", row.RelativeTime);
        _ = settings;
    }

    [Fact]
    public void Rows_DistinguishDroppedFromUnresolved()
    {
        var (vm, _) = Build(Watchlist(
            ("dropped", ExchangeCategory.Currency),
            ("missing", ExchangeCategory.Currency)));

        var snapshot = WithCategory(
            SnapshotBuilder.Category(
                ExchangeCategory.Currency,
                Now,
                [SnapshotBuilder.Price("divine", 200m, "Divine Orb")],
                [new ItemId("dropped")]));

        vm.Refresh(snapshot, Now);

        Assert.Collection(
            vm.Rows,
            dropped =>
            {
                // A skipped line means the item exists and could not be priced. Calling it "not
                // found" tells the user to delete something real (S2 10.5 D-PL5).
                Assert.Equal(RowKind.ItemDropped, dropped.Kind);
                Assert.Equal("price unavailable — item still exists", dropped.Price.Text);
            },
            missing =>
            {
                Assert.Equal(RowKind.ItemUnresolved, missing.Kind);
                Assert.Equal("item not found", missing.Price.Text);
            });
    }

    [Fact]
    public void Rows_AreLoadingBeforeAnyDataAndFailedAfterAFailure()
    {
        var (vm, _) = Build(Watchlist(("divine", ExchangeCategory.Currency)));

        vm.Refresh(Empty(), Now);
        Assert.Equal(RowKind.Loading, Assert.Single(vm.Rows).Kind);

        var failing = Empty() with
        {
            CategoryStatuses = new Dictionary<ExchangeCategory, CategoryStatus>
            {
                [ExchangeCategory.Currency] = new(
                    ExchangeCategory.Currency, 2, Now.AddMinutes(-4), null, null, null, 0, null, true),
            },
        };

        vm.Refresh(failing, Now);
        Assert.Equal(RowKind.FetchFailed, Assert.Single(vm.Rows).Kind);
        Assert.Equal(1, vm.FailedCategoryCount);
    }

    [Fact]
    public void RowsMarkStaleness_FromTheRawAgeNotTheFormattedText()
    {
        var (vm, _) = Build(Watchlist(("divine", ExchangeCategory.Currency)));
        var old = Now - Core.Pricing.StalenessPolicy.RowStaleAfter(5) - TimeSpan.FromMinutes(1);

        vm.Refresh(
            WithCategory(SnapshotBuilder.Category(
                ExchangeCategory.Currency, old, [SnapshotBuilder.Price("divine", 200m, "Divine Orb")])),
            Now);

        Assert.True(Assert.Single(vm.Rows).IsStale);
    }

    [Fact]
    public void BannersSurvive_EvenWhenRowFormattingThrows()
    {
        var (vm, _, localizer) = BuildWithLocalizer(Watchlist(("divine", ExchangeCategory.Currency)));
        localizer.ThrowOnItemName = true;

        var snapshot = SnapshotBuilder.WithConditions((AppConditionKind.SettingsCorrupt, null));

        // The pass fails, the fan-out isolates it, and the next pass retries — but the banner list
        // is computed first and on its own, so the one thing the user needs is already published
        // (S3 7.6 M5).
        Assert.Throws<InvalidOperationException>(() => vm.Refresh(snapshot, Now));
        Assert.Equal(AppConditionKind.SettingsCorrupt, Assert.Single(vm.Banners).Kind);
    }

    [Fact]
    public void MoreRowsText_NamesTheHeightSetting_OnlyWhenTheUserChoseTheHeight()
    {
        var (vm, settings) = Build();

        vm.HiddenRowCount = 3;
        Assert.Equal("+3 more", vm.MoreRowsText);

        settings.Update(settings.Current with
        {
            Window = settings.Current.Window with { HeightMode = HeightMode.Explicit },
        });
        vm.HiddenRowCount = 4;

        // S3 4.4.2: clipping the user caused reads as a defect unless the marker says otherwise.
        Assert.Equal("+4 more — adjust height in settings", vm.MoreRowsText);

        vm.HiddenRowCount = 0;
        Assert.Equal(string.Empty, vm.MoreRowsText);
    }

    [Fact]
    public void FooterCarriesTheAttribution_AndTheLatestFetchTime()
    {
        var (vm, _) = Build();

        vm.Refresh(WithCategory(SnapshotBuilder.Category(
            ExchangeCategory.Currency,
            Now.AddMinutes(-3),
            [SnapshotBuilder.Price("divine", 200m)])), Now);

        Assert.Equal("Data from poe.ninja — a community site, not affiliated with GGG", vm.FooterAttribution);
        Assert.Equal("3m ago", vm.FooterRelativeTime);
    }

    private static MarketSnapshot Empty() => SnapshotBuilder.Empty();

    private static MarketSnapshot WithCategory(CategorySnapshot category)
        => Empty() with
        {
            DataLeague = SnapshotBuilder.League,
            DataEpoch = 1,
            Categories = new Dictionary<ExchangeCategory, CategorySnapshot> { [category.Category] = category },
            Rate = new DivineRate(200m, SnapshotBuilder.Now, SnapshotBuilder.League, false),
        };

    private static EquatableArray<WatchlistEntry> Watchlist(params (string Id, ExchangeCategory Category)[] entries)
        => new(entries.Select(e =>
            new WatchlistEntry(new ItemId(e.Id), new CategoryRef(e.Category.ToString(), e.Category), null)));

    private static (OverlayViewModel Vm, FakeSettingsSource Settings) Build(
        EquatableArray<WatchlistEntry>? watchlist = null)
    {
        var (vm, settings, _) = BuildWithLocalizer(watchlist);
        return (vm, settings);
    }

    private static (OverlayViewModel Vm, FakeSettingsSource Settings, FakeLocalizer Localizer) BuildWithLocalizer(
        EquatableArray<WatchlistEntry>? watchlist = null)
    {
        var localizer = new FakeLocalizer();
        var settings = new FakeSettingsSource(
            watchlist is null ? AppSettings.Default : AppSettings.Default with { Watchlist = watchlist });

        var vm = new OverlayViewModel(
            localizer,
            settings,
            new FakeTimeProvider(Now),
            new RecordingLogger<OverlayViewModel>());

        return (vm, settings, localizer);
    }
}
