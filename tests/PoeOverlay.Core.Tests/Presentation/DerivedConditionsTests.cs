using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.UiState;
using PoeOverlay.Core.Presentation.ViewModels.Rows;
using PoeOverlay.Core.Pricing;
using Xunit;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// S3 9.2 / S4 11.8, 18.4 — the four pure functions and the two-branch stall verdict.
/// </summary>
public sealed class DerivedConditionsTests
{
    private const int Interval = 5;

    private static readonly DateTimeOffset Now = new(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);

    private static readonly Heartbeat Fresh = new(Now, 3, Now, RoundOutcome.Completed, false, null, null);

    [Fact]
    public void PollingStopped_IsFalse_WhenNoRoundHasEverBeenAttempted()
    {
        var never = new Heartbeat(null, 0, null, null, false, null, null);

        // PL0: default(DateTimeOffset) is year 0001. Treating absence as an instant puts a
        // "polling stopped" banner next to a Loading row on the first 30 s tick.
        Assert.False(DerivedConditions.IsPollingStopped(never, Now.AddYears(1), Interval));
        Assert.Equal((false, false), DerivedConditions.PollingStoppedBranch(never, Now.AddYears(1), Interval));
    }

    [Fact]
    public void PollingStopped_IsFalse_JustInsideTheThreshold()
    {
        var stale = StalenessPolicy.HeartbeatStaleAfter(Interval);
        var heartbeat = Fresh with { LastRoundAttemptAt = Now - stale };

        Assert.False(DerivedConditions.IsPollingStopped(heartbeat, Now, Interval));
    }

    [Fact]
    public void PollingStopped_IsTrue_JustOutsideTheThreshold()
    {
        var stale = StalenessPolicy.HeartbeatStaleAfter(Interval);
        var heartbeat = Fresh with { LastRoundAttemptAt = Now - stale - TimeSpan.FromTicks(1) };

        Assert.Equal((true, false), DerivedConditions.PollingStoppedBranch(heartbeat, Now, Interval));
    }

    [Fact]
    public void PollingStopped_LoopExited_IsTheOtherBranch_RegardlessOfAge()
    {
        var exited = Fresh with { LoopExited = true, ExitKind = LoopExitKind.Faulted, ExitedAt = Now };

        // The two branches are different problems: the stale branch clears itself on the next
        // heartbeat, this one only on a restart (S3 2.2 D-SH2).
        Assert.Equal((true, true), DerivedConditions.PollingStoppedBranch(exited, Now, Interval));
    }

    [Fact]
    public void RatePending_IsTrue_WhenThereIsNoRate()
        => Assert.True(DerivedConditions.IsRatePending(null, Now, Interval));

    [Fact]
    public void RatePending_IsFalse_JustInsideTheMaxAge()
    {
        var rate = new DivineRate(200m, Now - StalenessPolicy.RateMaxAge(Interval), "Allflame", false);

        Assert.False(DerivedConditions.IsRatePending(rate, Now, Interval));
    }

    [Fact]
    public void RatePending_IsTrue_JustOutsideTheMaxAge()
    {
        var rate = new DivineRate(
            200m,
            Now - StalenessPolicy.RateMaxAge(Interval) - TimeSpan.FromTicks(1),
            "Allflame",
            false);

        Assert.True(DerivedConditions.IsRatePending(rate, Now, Interval));
    }

    [Fact]
    public void RatePendingDuration_IsMeasuredFromExpiry_NotFromAcquisition()
    {
        var maxAge = StalenessPolicy.RateMaxAge(Interval);
        var rate = new DivineRate(200m, Now - maxAge - TimeSpan.FromMinutes(3), "Allflame", false);

        // "rate pending for 33m" when the rate expired three minutes ago would be a lie about how
        // long the user has been without a usable rate (S2 10.5).
        Assert.Equal(TimeSpan.FromMinutes(3), DerivedConditions.RatePendingDuration(rate, Now, Interval));
    }

    [Fact]
    public void RatePendingDuration_IsZero_BeforeExpiry()
    {
        var rate = new DivineRate(200m, Now, "Allflame", false);

        Assert.Equal(TimeSpan.Zero, DerivedConditions.RatePendingDuration(rate, Now, Interval));
    }

    [Fact]
    public void RowStale_IsARawTimeSpanComparison_AtTheBoundary()
    {
        var stale = StalenessPolicy.RowStaleAfter(Interval);

        Assert.False(DerivedConditions.IsRowStale(Now - stale, Now, Interval));
        Assert.True(DerivedConditions.IsRowStale(Now - stale - TimeSpan.FromTicks(1), Now, Interval));
    }

    [Theory]
    [InlineData(false, false, false, RowKind.Loading)]
    [InlineData(false, true, false, RowKind.FetchFailed)]
    [InlineData(true, false, true, RowKind.ItemDropped)]
    [InlineData(true, false, false, RowKind.ItemUnresolved)]
    [InlineData(true, true, true, RowKind.ItemDropped)]
    public void ClassifyRow_CoversTheFourBranches(
        bool hasSnapshotEntry,
        bool failing,
        bool skipped,
        RowKind expected)
        => Assert.Equal(expected, DerivedConditions.ClassifyRow(hasSnapshotEntry, failing, skipped));

    [Fact]
    public void ClassifyRow_SkippedItem_IsNeverReportedAsMissing()
    {
        // primaryValue: 0 is an ordinary state — nothing listed. The line is skipped and the item
        // disappears from a *successful* snapshot; a two-way split would tell the user to delete
        // an item that exists (S2 10.5 D-PL5).
        Assert.Equal(RowKind.ItemDropped, DerivedConditions.ClassifyRow(true, false, true));
        Assert.NotEqual(RowKind.ItemUnresolved, DerivedConditions.ClassifyRow(true, false, true));
    }

    [Fact]
    public void DisplayState_IsLoading_UntilTheFirstRoundCompletes()
    {
        var running = new Heartbeat(Now, 1, null, null, false, null, null);

        Assert.Equal(DisplayState.Loading, DerivedConditions.ClassifyDisplayState(running));
    }

    [Theory]
    [InlineData(RoundOutcome.Completed, DisplayState.Ready)]
    [InlineData(RoundOutcome.PartiallyFailed, DisplayState.Ready)]
    [InlineData(RoundOutcome.Canceled, DisplayState.Ready)]
    [InlineData(RoundOutcome.AllFailed, DisplayState.Failed)]
    [InlineData(RoundOutcome.LeagueUnresolved, DisplayState.Failed)]
    public void DisplayState_FollowsTheLastOutcome_AndNeverReturnsToLoading(
        RoundOutcome outcome,
        DisplayState expected)
    {
        var heartbeat = Fresh with { LastOutcome = outcome };

        Assert.Equal(expected, DerivedConditions.ClassifyDisplayState(heartbeat));
    }

    [Fact]
    public void Duration_FormatsWithoutTheRelativeSuffix()
    {
        // "rate pending for 3m ago" is what reusing PricingEngine.Relative here would render
        // (S4 11.8 D1 caught the same class of defect in the constants).
        Assert.Equal("45s", UiStateFormat.Duration(TimeSpan.FromSeconds(45)));
        Assert.Equal("3m", UiStateFormat.Duration(TimeSpan.FromMinutes(3)));
        Assert.Equal("2h", UiStateFormat.Duration(TimeSpan.FromHours(2)));
        Assert.Equal("4d", UiStateFormat.Duration(TimeSpan.FromDays(4)));
        Assert.Equal("0s", UiStateFormat.Duration(TimeSpan.FromSeconds(-5)));
    }
}
