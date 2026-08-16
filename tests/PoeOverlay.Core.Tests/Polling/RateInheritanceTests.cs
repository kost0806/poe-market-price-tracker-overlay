using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Pricing;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL9 – PL12 (S4 16.5) — rate extraction and inheritance.
/// </summary>
public sealed class RateInheritanceTests
{
    private static readonly RoundContext Context =
        new(PollingTestHarness.League, 1, 1, 1, PollingTestHarness.Start);

    private static DivineRate Previous(string league = PollingTestHarness.League)
        => new(194.6m, PollingTestHarness.Start, league, false);

    [Fact]
    public async Task Extraction_ReadsTheDivineLineAndTakesItsFetchInstant()
    {
        using var harness = await PollingHarness.CreateAsync();
        var fetchedAt = PollingTestHarness.Start.AddMinutes(-3);
        var result = PollingTestHarness.Ok(
            PollingTestHarness.Snapshot(ExchangeCategory.Currency, fetchedAt: fetchedAt, divineValue: 200m));

        var rate = harness.Service.InheritOrExtractRate(result, null, Context, StalenessPolicy.RateMaxAge(5));

        Assert.NotNull(rate);
        Assert.Equal(200m, rate.ChaosPerDivine);
        Assert.False(rate.Inherited);

        // FetchedAt, not the clock: two values taken from the same response must share one
        // acquisition instant or Pricing's age min() disagrees with itself.
        Assert.Equal(fetchedAt, rate.AcquiredAt);
    }

    [Fact]
    public async Task PL9_WhenCurrencyFailsAndThePreviousRateIsYoungEnough_ItIsInherited()
    {
        using var harness = await PollingHarness.CreateAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(25));

        var rate = harness.Service.InheritOrExtractRate(
            PollingTestHarness.Fail(), Previous(), Context, StalenessPolicy.RateMaxAge(5));

        Assert.NotNull(rate);
        Assert.True(rate.Inherited);
        Assert.Equal(194.6m, rate.ChaosPerDivine);

        // The single most important invariant of the record: inheritance rewrites Inherited and
        // nothing else.
        Assert.Equal(PollingTestHarness.Start, rate.AcquiredAt);
    }

    [Fact]
    public async Task PL10_APreviousRatePastTheMaximumAge_IsNotInherited()
    {
        using var harness = await PollingHarness.CreateAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(31));

        Assert.Null(harness.Service.InheritOrExtractRate(
            PollingTestHarness.Fail(), Previous(), Context, StalenessPolicy.RateMaxAge(5)));
    }

    [Fact]
    public async Task PL11_APreviousRateFromAnotherLeague_IsNotInherited()
    {
        using var harness = await PollingHarness.CreateAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.Null(harness.Service.InheritOrExtractRate(
            PollingTestHarness.Fail(), Previous("Standard"), Context, StalenessPolicy.RateMaxAge(5)));
    }

    [Fact]
    public async Task PL12_RepeatedInheritance_DoesNotPostponeExpiry()
    {
        using var harness = await PollingHarness.CreateAsync();
        var maxAge = StalenessPolicy.RateMaxAge(5);
        Assert.Equal(TimeSpan.FromMinutes(30), maxAge);

        var rate = Previous();
        foreach (var minute in new[] { 10, 20, 30 })
        {
            harness.Time.SetUtcNow(PollingTestHarness.Start.AddMinutes(minute));
            rate = harness.Service.InheritOrExtractRate(PollingTestHarness.Fail(), rate, Context, maxAge)!;

            Assert.NotNull(rate);
            Assert.True(rate.Inherited);
            Assert.Equal(PollingTestHarness.Start, rate.AcquiredAt);
        }

        // Had any of those three inheritances refreshed AcquiredAt, this rate would still be alive
        // here — and would stay alive for as long as the Currency endpoint stayed down, which is
        // exactly the expiry D16 exists to enforce.
        harness.Time.SetUtcNow(PollingTestHarness.Start.AddMinutes(35));
        Assert.Null(harness.Service.InheritOrExtractRate(PollingTestHarness.Fail(), rate, Context, maxAge));
    }

    [Fact]
    public async Task AnAlreadyInheritedRate_IsReturnedUnchangedRatherThanReissued()
    {
        using var harness = await PollingHarness.CreateAsync();
        var inherited = Previous() with { Inherited = true };
        harness.Time.Advance(TimeSpan.FromMinutes(5));

        Assert.Same(
            inherited,
            harness.Service.InheritOrExtractRate(
                PollingTestHarness.Fail(), inherited, Context, StalenessPolicy.RateMaxAge(5)));
    }

    [Fact]
    public async Task ACurrencyResponseWithNoDivineLine_FallsBackToInheritance()
    {
        using var harness = await PollingHarness.CreateAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));

        var result = PollingTestHarness.Ok(PollingTestHarness.CurrencyWithoutDivine());
        var rate = harness.Service.InheritOrExtractRate(
            result, Previous(), Context, StalenessPolicy.RateMaxAge(5));

        Assert.NotNull(rate);
        Assert.True(rate.Inherited);
    }

    [Fact]
    public async Task ARoundThatSucceeds_CommitsAFreshRate()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Rate is not null, "the rate was committed");

        var rate = harness.Current.Rate!;
        Assert.Equal(194.6m, rate.ChaosPerDivine);
        Assert.False(rate.Inherited);
        Assert.Equal(PollingTestHarness.League, rate.League);
    }

    [Fact]
    public async Task ARoundWhereCurrencyFails_InheritsWithoutBlockingOtherCommits()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        harness.Market.Respond = (category, call) => category == ExchangeCategory.Currency && call > 0
            ? PollingTestHarness.Fail()
            : PollingTestHarness.Ok(PollingTestHarness.Snapshot(category));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Rate is not null, "the first rate landed");

        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(s => s.Rate!.Inherited, "the rate was inherited");

        // The rate never gates a commit (D1): its absence is a display state, not a round failure.
        Assert.True(harness.Current.Categories.ContainsKey(ExchangeCategory.Scarab));
        Assert.True(harness.Current.Rate!.Inherited);
    }
}
