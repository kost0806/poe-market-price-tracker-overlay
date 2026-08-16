using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL8 / PL8' (S4 16.5) — D8-e forced acceptance and its latch reset.
/// </summary>
public sealed class ForcedAcceptanceTests
{
    [Fact]
    public async Task PL8_TheThirdConsecutiveJump_IsAcceptedAndSaysSoLoudly()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        harness.Market.Respond = (category, call) => PollingTestHarness.Ok(
            PollingTestHarness.Snapshot(
                category,
                median: category == ExchangeCategory.Scarab && call > 0 ? 60m : 10m));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.ContainsKey(ExchangeCategory.Scarab), "the baseline landed");

        // Every rejection also earns a cooldown (interval x 1, then x 2, then x 4), so the rounds
        // that can actually re-attempt Scarab are t=5, t=10 and t=20.
        foreach (var minutes in new[] { 5, 5, 5, 5 })
        {
            harness.Time.Advance(TimeSpan.FromMinutes(minutes));
            await harness.RunRoundAsync(harness.Rounds.Count + 1);
        }

        await harness.WaitForAsync(
            s => s.Categories[ExchangeCategory.Scarab].MedianPrimaryValue == 60m, "the jump was force-accepted");

        var snapshot = harness.Current;
        var status = snapshot.CategoryStatuses[ExchangeCategory.Scarab];

        Assert.True(snapshot.Categories[ExchangeCategory.Scarab].ValidationBypassed);
        Assert.NotNull(status.LastForcedAcceptAt);
        Assert.Equal("MedianJumpForcedAccept", snapshot.LastError!.Code);
        Assert.Contains(
            harness.Logger.WithCode("MedianJumpForcedAccept"),
            entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task PL8Prime_AfterAForcedAccept_TheJumpLatchIsBackToZero()
    {
        using var harness = await PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

        harness.Market.Respond = (category, call) => PollingTestHarness.Ok(
            PollingTestHarness.Snapshot(
                category,
                median: category == ExchangeCategory.Scarab && call > 0 ? 60m : 10m));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.ContainsKey(ExchangeCategory.Scarab), "the baseline landed");

        for (var i = 0; i < 5; i++)
        {
            harness.Time.Advance(TimeSpan.FromMinutes(5));
            await harness.RunRoundAsync(harness.Rounds.Count + 1);
        }

        await harness.WaitForAsync(
            s => s.CategoryStatuses[ExchangeCategory.Scarab].ConsecutiveMedianJumps == 0
                && s.Categories[ExchangeCategory.Scarab].MedianPrimaryValue == 60m,
            "the latch was reset by a clean success");

        // Without the reset a category that jumped twice would have D8-e switched off for the rest
        // of the session, so the check that exists to catch a schema change would be silently gone.
        Assert.Equal(0, harness.Current.CategoryStatuses[ExchangeCategory.Scarab].ConsecutiveMedianJumps);
        Assert.Equal(0, harness.Current.CategoryStatuses[ExchangeCategory.Scarab].ConsecutiveFailures);
    }
}
