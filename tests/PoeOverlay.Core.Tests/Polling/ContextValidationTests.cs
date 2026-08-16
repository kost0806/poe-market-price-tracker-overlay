using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Polling;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// S2 11.9 PL5 – PL7, PL25 (S4 16.5) — the two context checks D8-c and D8-e.
/// </summary>
public sealed class ContextValidationTests
{
    private static Task<PollingHarness> ScarabHarnessAsync()
        => PollingHarness.CreateAsync(
            PollingTestHarness.Settings(watchlist: ("rusted", ExchangeCategory.Scarab)));

    [Fact]
    public async Task PL5_CurrencyWithoutADivineLine_FailsOnItsOwnAndTheRestStillCommits()
    {
        using var harness = await ScarabHarnessAsync();
        harness.Market.Respond = (category, _) => category == ExchangeCategory.Currency
            ? PollingTestHarness.Ok(PollingTestHarness.CurrencyWithoutDivine())
            : PollingTestHarness.Ok(PollingTestHarness.Snapshot(category));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.ContainsKey(ExchangeCategory.Scarab), "Scarab committed");

        var snapshot = harness.Current;

        // The whole Currency response is impugned, not just the rate: a response that lost its
        // anchor is not half-good data.
        Assert.False(snapshot.Categories.ContainsKey(ExchangeCategory.Currency));
        Assert.Equal(
            FailureKind.DivineLineMissing,
            snapshot.CategoryStatuses[ExchangeCategory.Currency].LastFailure!.Kind);
        Assert.True(snapshot.Categories.ContainsKey(ExchangeCategory.Scarab));
        Assert.Equal(RoundOutcome.PartiallyFailed, harness.Rounds[0].Outcome);
    }

    [Fact]
    public async Task PL6_WithNoPreviousSnapshot_TheMedianCheckPasses()
    {
        using var harness = await ScarabHarnessAsync();
        harness.Market.Respond = (category, _) =>
            PollingTestHarness.Ok(PollingTestHarness.Snapshot(category, median: 100_000m));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.ContainsKey(ExchangeCategory.Scarab), "Scarab committed");

        // Reading "compare against the previous median" as "an absent one cannot be compared, so
        // reject" would leave the application without a single value, forever.
        Assert.Equal(100_000m, harness.Current.Categories[ExchangeCategory.Scarab].MedianPrimaryValue);
        Assert.Equal(RoundOutcome.Completed, harness.Rounds[0].Outcome);
    }

    [Fact]
    public async Task PL7_AMedianThatMovesSixfold_IsRejectedAndTheOldValueSurvives()
    {
        using var harness = await ScarabHarnessAsync();
        harness.Market.Respond = (category, call) => PollingTestHarness.Ok(
            PollingTestHarness.Snapshot(category, median: category == ExchangeCategory.Scarab && call > 0 ? 60m : 10m));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.ContainsKey(ExchangeCategory.Scarab), "Scarab committed");

        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(
            s => s.CategoryStatuses[ExchangeCategory.Scarab].ConsecutiveMedianJumps == 1, "the jump was counted");

        var snapshot = harness.Current;
        Assert.Equal(10m, snapshot.Categories[ExchangeCategory.Scarab].MedianPrimaryValue);
        Assert.Equal(FailureKind.MedianJump, snapshot.CategoryStatuses[ExchangeCategory.Scarab].LastFailure!.Kind);
        Assert.Equal(1, snapshot.CategoryStatuses[ExchangeCategory.Scarab].ConsecutiveMedianJumps);
    }

    [Theory]
    [InlineData(10, 60, 0, false)]
    [InlineData(10, 50, 0, true)]
    [InlineData(60, 10, 0, false)]
    [InlineData(10, 60, 1, false)]
    [InlineData(10, 60, 2, true)]
    [InlineData(10, 1000, 3, true)]
    public void IsMedianJumpAcceptable_FollowsTheRatioAndTheForcedAcceptanceLatch(
        decimal previous, decimal current, int jumps, bool acceptable)
    {
        Assert.Equal(acceptable, PollingService.IsMedianJumpAcceptable(current, previous, jumps));
    }

    [Fact]
    public void IsMedianJumpAcceptable_WithNoBaseline_IsAlwaysAcceptable()
    {
        Assert.True(PollingService.IsMedianJumpAcceptable(1_000_000m, null, 0));
        Assert.True(PollingService.IsMedianJumpAcceptable(0.0001m, null, 0));
    }

    [Fact]
    public void IsMedianJump_ExactlyAtTheRatio_IsNotAJump()
    {
        // The rule is "greater than five", so a fivefold move is still accepted; writing >= would
        // reject an ordinary league-start price move.
        Assert.False(PollingService.IsMedianJump(50m, 10m));
        Assert.True(PollingService.IsMedianJump(50.01m, 10m));
    }

    [Fact]
    public async Task PL25_RecordingAFailure_LeavesTheCommittedDataUntouched()
    {
        using var harness = await ScarabHarnessAsync();
        harness.Market.Respond = (category, call) => category == ExchangeCategory.Scarab && call > 0
            ? PollingTestHarness.Fail()
            : PollingTestHarness.Ok(PollingTestHarness.Snapshot(category));

        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Categories.ContainsKey(ExchangeCategory.Scarab), "Scarab committed");
        var committed = harness.Current.Categories[ExchangeCategory.Scarab];

        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(
            s => s.CategoryStatuses[ExchangeCategory.Scarab].ConsecutiveFailures == 1, "the failure landed");

        // FR-03-3 holds structurally, not by discipline: the failure path can only reach the status.
        Assert.Same(committed, harness.Current.Categories[ExchangeCategory.Scarab]);
        Assert.NotNull(harness.Current.CategoryStatuses[ExchangeCategory.Scarab].LastFailure);
    }
}
