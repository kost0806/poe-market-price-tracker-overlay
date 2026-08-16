using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Polling;
using PoeOverlay.Core.Settings;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL15 – PL17 (S4 16.5) — the repoll diff, its debounce and its floor.
/// </summary>
public sealed class RepollDebounceTests
{
    private static AppSettings WithWatchlist(AppSettings settings, params ExchangeCategory[] categories)
        => settings with
        {
            Watchlist = new EquatableArray<WatchlistEntry>(categories.Select((c, i) => new WatchlistEntry(
                new ItemId($"item{i}"), new CategoryRef(c.ToString(), c), null))),
        };

    [Fact]
    public async Task PL15_AddingAnItemInAnAlreadyCachedCategory_DoesNotRepoll()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.ContainsKey(ExchangeCategory.Scarab), "Scarab cached");

        harness.Settings.Update(WithWatchlist(
            harness.Settings.Current, ExchangeCategory.Scarab, ExchangeCategory.Scarab));

        // Well past the two-second debounce and the sixty-second floor.
        harness.Time.Advance(TimeSpan.FromSeconds(120));

        // The next round can only be the scheduled one, and the count proves no repoll slipped in
        // between: a repoll would have made this round three rather than two.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);

        Assert.Equal(2, harness.Rounds.Count);
    }

    [Fact]
    public async Task PL16_FiveEditsInsideTheWindowAfterTheFloor_ProduceExactlyOneRound()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        harness.Time.Advance(TimeSpan.FromSeconds(90));

        var categories = new[]
        {
            ExchangeCategory.Scarab, ExchangeCategory.Fossil, ExchangeCategory.Essence,
            ExchangeCategory.Oil, ExchangeCategory.Artifact,
        };

        for (var i = 1; i <= categories.Length; i++)
        {
            harness.Settings.Update(WithWatchlist(harness.Settings.Current, categories[..i]));
            harness.Time.Advance(TimeSpan.FromMilliseconds(100));
        }

        harness.Time.Advance(PollingOptions.RepollDebounceWindow);
        await harness.RunRoundAsync(2);

        // Five edits, one round: the window collapses them, and the floor was already satisfied by
        // the ninety seconds since the previous round finished.
        Assert.Equal(2, harness.Rounds.Count);
        Assert.Equal(6, harness.Market.Rounds[1].Count);

        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(3);
        Assert.Equal(3, harness.Rounds.Count);
    }

    [Fact]
    public async Task PL17_ARequestInsideTheFloor_IsDelayedRatherThanDropped()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        harness.Time.Advance(TimeSpan.FromSeconds(10));
        harness.Settings.Update(WithWatchlist(harness.Settings.Current, ExchangeCategory.Scarab));

        // The debounce elapses at t=12 s, but the floor is not reached until t=60 s.
        harness.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(harness.Rounds);

        harness.Time.Advance(TimeSpan.FromSeconds(45));
        await harness.RunRoundAsync(2);

        // Dropping it instead of delaying it is how an edit silently becomes a no-op.
        Assert.Equal(2, harness.Rounds.Count);
        Assert.Equal(PollingTestHarness.Start.AddSeconds(60), harness.Time.GetUtcNow());
        Assert.Contains(ExchangeCategory.Scarab, harness.Market.Rounds[1]);
    }

    [Fact]
    public void RequiresImmediateRepoll_OnlyWhenANewCategoryIsNotAlreadyCached()
    {
        var empty = AppSettings.Default;
        var withScarab = WithWatchlist(empty, ExchangeCategory.Scarab);
        var withBoth = WithWatchlist(empty, ExchangeCategory.Scarab, ExchangeCategory.Fossil);

        var cached = new Dictionary<ExchangeCategory, CategorySnapshot>
        {
            [ExchangeCategory.Scarab] = PollingTestHarness.Snapshot(ExchangeCategory.Scarab),
        };

        Assert.True(PollingService.RequiresImmediateRepoll(empty, withScarab, new Dictionary<ExchangeCategory, CategorySnapshot>()));
        Assert.False(PollingService.RequiresImmediateRepoll(empty, withScarab, cached));
        Assert.True(PollingService.RequiresImmediateRepoll(withScarab, withBoth, cached));

        // A removal never needs new data, and neither does re-adding something already held.
        Assert.False(PollingService.RequiresImmediateRepoll(withBoth, withScarab, cached));
        Assert.False(PollingService.RequiresImmediateRepoll(withScarab, withScarab, cached));
    }
}
