using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Polling;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL13 / PL14 (S4 16.5) — the per-category cooldown.
/// </summary>
public sealed class CooldownTests
{
    [Theory]
    [InlineData(1, 5, 5)]
    [InlineData(2, 5, 10)]
    [InlineData(3, 5, 20)]
    [InlineData(4, 5, 40)]
    [InlineData(5, 5, 40)]
    [InlineData(10, 5, 40)]
    [InlineData(64, 5, 40)]
    [InlineData(3, 60, 240)]
    public void PL14_TheMultiplierDoublesAndThenStopsAtEight(int failures, int interval, int expectedMinutes)
    {
        // The exponent is clamped before the shift. An unclamped 1 << (failures - 1) wraps modulo 32
        // once a long outage pushes the count past 32, which would hand back a *shorter* cooldown
        // the longer the endpoint had been down.
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            PollingService.ComputeCooldown(failures, interval));
    }

    [Fact]
    public void ComputeCooldown_NeverExcludesPermanently()
    {
        Assert.True(PollingService.ComputeCooldown(int.MaxValue, 60) < TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task PL13_AfterThreeConsecutiveFailures_TheStatusCarriesTheFourfoldCooldown()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        harness.Market.Respond = (category, _) => category == ExchangeCategory.Scarab
            ? PollingTestHarness.Fail()
            : PollingTestHarness.Ok(PollingTestHarness.Snapshot(category));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(
            s => s.CategoryStatuses.ContainsKey(ExchangeCategory.Scarab), "the first failure landed");

        // Failure 1 cools down for one interval, so the round at t=5 may re-attempt; failure 2 cools
        // down for two, so the next attempt is at t=15.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(3);
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(4);

        await harness.WaitForAsync(
            s => s.CategoryStatuses[ExchangeCategory.Scarab].ConsecutiveFailures == 3, "three failures");

        var attemptedAt = PollingTestHarness.Start.AddMinutes(15);
        var status = harness.Current.CategoryStatuses[ExchangeCategory.Scarab];

        Assert.Equal(3, status.ConsecutiveFailures);
        Assert.Equal(attemptedAt, status.LastAttemptAt);
        Assert.Equal(attemptedAt + TimeSpan.FromMinutes(20), status.CooldownUntil);

        // Round 3 (t=10) skipped Scarab because it was still cooling down, and skipping is not
        // failing — counting it would let the cooldown extend itself indefinitely.
        Assert.Equal(new[] { ExchangeCategory.Currency }, harness.Market.Rounds[2]);
    }

    [Fact]
    public async Task ASuccessfulFetch_ClearsTheCooldown()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        harness.Market.Respond = (category, call) => category == ExchangeCategory.Scarab && call == 0
            ? PollingTestHarness.Fail()
            : PollingTestHarness.Ok(PollingTestHarness.Snapshot(category));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(
            s => s.CategoryStatuses[ExchangeCategory.Scarab].CooldownUntil is not null, "the cooldown was set");

        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(
            s => s.Categories.ContainsKey(ExchangeCategory.Scarab), "the retry succeeded");

        var status = harness.Current.CategoryStatuses[ExchangeCategory.Scarab];
        Assert.Null(status.CooldownUntil);
        Assert.Equal(0, status.ConsecutiveFailures);
    }

    [Fact]
    public void ResolveCategorySet_AlwaysContainsCurrencyAndSortsItFirst()
    {
        var watchlist = new EquatableArray<WatchlistEntry>(
        [
            new WatchlistEntry(new ItemId("fossil"), new CategoryRef("Fossil", ExchangeCategory.Fossil), null),
            new WatchlistEntry(new ItemId("rusted"), new CategoryRef("Scarab", ExchangeCategory.Scarab), null),
            new WatchlistEntry(new ItemId("dup"), new CategoryRef("Scarab", ExchangeCategory.Scarab), null),
            new WatchlistEntry(new ItemId("unknown"), new CategoryRef("Chisel", null), null),
        ]);

        var set = PollingService.ResolveCategorySet(
            watchlist,
            new Dictionary<ExchangeCategory, CategoryStatus>(),
            PollingTestHarness.Start);

        // Securing the rate first means fewer categories in the same round have to fall back to an
        // inherited one, and the unresolved entry is a settings problem rather than a fetch.
        Assert.Equal(
            new[] { ExchangeCategory.Currency, ExchangeCategory.Scarab, ExchangeCategory.Fossil },
            set);
    }

    [Fact]
    public void ResolveCategorySet_ExcludesCoolingCategoriesButIsNeverEmpty()
    {
        var watchlist = new EquatableArray<WatchlistEntry>(
        [
            new WatchlistEntry(new ItemId("rusted"), new CategoryRef("Scarab", ExchangeCategory.Scarab), null),
        ]);

        var now = PollingTestHarness.Start;
        var statuses = new Dictionary<ExchangeCategory, CategoryStatus>
        {
            [ExchangeCategory.Currency] = Cooling(ExchangeCategory.Currency, now.AddMinutes(40)),
            [ExchangeCategory.Scarab] = Cooling(ExchangeCategory.Scarab, now.AddMinutes(10)),
        };

        // Currency is not exempt from cooldown, but an entirely empty round would land no commits at
        // all — and the store reads a run of commit-free rounds as evidence that commits are being
        // rejected. Keeping the soonest-expiring candidate costs one request per interval.
        Assert.Equal(new[] { ExchangeCategory.Scarab }, PollingService.ResolveCategorySet(watchlist, statuses, now));

        // A cooldown that has just expired is over: the boundary is inclusive.
        Assert.Equal(
            new[] { ExchangeCategory.Scarab },
            PollingService.ResolveCategorySet(watchlist, statuses, now.AddMinutes(10)));
    }

    private static CategoryStatus Cooling(ExchangeCategory category, DateTimeOffset until)
        => new(category, 1, PollingTestHarness.Start, null, until, null, 0, null, true);
}
