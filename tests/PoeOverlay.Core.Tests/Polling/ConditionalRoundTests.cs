using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Polling;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 7.3 step 8 / HLD D24 — a round revalidates against what the store already holds.
/// </summary>
public sealed class ConditionalRoundTests
{
    [Fact]
    public async Task ASecondRound_HandsMarketTheSnapshotTheStoreIsHolding()
    {
        using var harness = await PollingHarness.CreateAsync(PollingTestHarness.Settings());
        await harness.StartAsync();

        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.Count > 0, "the first round committed");

        // Nothing was held the first time round, so that request had to be unconditional.
        Assert.Null(harness.Market.HeldFor(ExchangeCategory.Currency));

        var committed = harness.Current.Categories[ExchangeCategory.Currency];

        harness.Time.Advance(TimeSpan.FromMinutes(20));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(
            _ => harness.Market.HeldFor(ExchangeCategory.Currency) is not null,
            "the second round asked conditionally");

        // The committed snapshot itself, not a copy assembled from it: the ETag has to travel with
        // the data it validates, which is the whole of D24.
        Assert.Same(committed, harness.Market.HeldFor(ExchangeCategory.Currency));
    }

    [Fact]
    public async Task ALeagueChange_MakesTheNextRoundUnconditional()
    {
        using var harness = await PollingHarness.CreateAsync(PollingTestHarness.Settings());
        await harness.StartAsync();

        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.Count > 0, "the first league landed");

        harness.Time.Advance(TimeSpan.FromSeconds(60));
        harness.Settings.Update(harness.Settings.Current with { League = "Standard" });
        harness.Time.Advance(PollingOptions.RepollDebounceWindow);

        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(s => s.DataLeague == "Standard", "the new league was committed");

        // A validator only means anything for the URL it came from, and the URL carries the league.
        // Nobody clears anything for this to hold: the round's baseline is empty after a transition.
        Assert.Null(harness.Market.HeldFor(ExchangeCategory.Currency));
    }
}
