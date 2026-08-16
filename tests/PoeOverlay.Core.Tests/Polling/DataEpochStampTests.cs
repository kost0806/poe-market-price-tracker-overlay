using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// INV-2 — Polling stamps the round's epoch onto every snapshot before committing it.
/// </summary>
/// <remarks>
/// <c>IMarketClient.FetchCategoryAsync</c> has no epoch parameter and Market has no other way to
/// learn one, so every snapshot leaves that module with <c>DataEpoch = 0</c>. Commit validation
/// compares the command's <c>DataTag</c>, not the snapshot inside it, so an unstamped snapshot is
/// accepted without complaint and INV-2 becomes false with nothing reporting it — the failure is
/// silent, not loud.
/// </remarks>
public sealed class DataEpochStampTests
{
    [Fact]
    public void TheSnapshotsMarketProduces_CarryEpochZero()
    {
        Assert.Equal(0, PollingTestHarness.Snapshot(ExchangeCategory.Currency).DataEpoch);
    }

    [Fact]
    public async Task EveryCommittedSnapshot_CarriesTheStoresEpoch()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.Count == 2, "both categories landed");

        var snapshot = harness.Current;
        Assert.Equal(1, snapshot.DataEpoch);
        Assert.All(snapshot.Categories.Values, c => Assert.Equal(snapshot.DataEpoch, c.DataEpoch));
    }

    [Fact]
    public async Task AfterASecondLeagueTransition_TheStampFollowsTheNewEpoch()
    {
        using var harness = await PollingHarness.CreateAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.Count == 1, "the first league landed");

        harness.Time.Advance(TimeSpan.FromSeconds(61));
        harness.Settings.Update(harness.Settings.Current with { League = "Standard" });
        harness.Time.Advance(TimeSpan.FromSeconds(3));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(s => s.DataEpoch == 2 && s.Categories.Count == 1, "the second league landed");

        var snapshot = harness.Current;
        Assert.Equal(2, snapshot.DataEpoch);
        Assert.All(snapshot.Categories.Values, c => Assert.Equal(2, c.DataEpoch));

        // The epoch rises only on a league change (INV-7), so an ordinary scheduled round leaves it
        // where it is and the previously committed snapshots stay valid.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(3);
        await harness.WaitForAsync(s => s.Heartbeat.LastRoundNumber == 3, "round three recorded");

        Assert.Equal(2, harness.Current.DataEpoch);
        Assert.All(harness.Current.Categories.Values, c => Assert.Equal(2, c.DataEpoch));
        Assert.Equal(0, harness.Current.RejectedCommitCount);
    }
}
