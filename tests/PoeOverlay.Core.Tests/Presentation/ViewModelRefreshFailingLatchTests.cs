using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Tests.TestSupport;
using Xunit;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// S3 10.1 / D-PS10 (B3) — the condition is raised on the boundary and only on the boundary.
/// </summary>
/// <remarks>
/// The assertions are on which call was made, not on how many were made in total. A tally would
/// pass under an implementation that raised the condition on the wrong pass, or that raised and
/// cleared it alternately; the kind and the <c>active</c> flag are what discriminate.
/// </remarks>
public sealed class ViewModelRefreshFailingLatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BelowTheThreshold_NothingIsRaised()
    {
        var (store, _, fanout) = Build(out var subscriber);
        using var _fanout = fanout;
        subscriber.ThrowOnRefresh = true;

        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold - 1; i++)
        {
            store.Publish();
        }

        Assert.Empty(store.ConditionCalls);
    }

    [Fact]
    public void OnTheThresholdPass_TheConditionIsRaisedExactlyOnce()
    {
        var (store, _, fanout) = Build(out var subscriber);
        using var _fanout = fanout;
        subscriber.ThrowOnRefresh = true;

        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold; i++)
        {
            store.Publish();
        }

        var call = Assert.Single(store.ConditionCalls);
        Assert.Equal(AppConditionKind.ViewModelRefreshFailing, call.Kind);
        Assert.True(call.Active);
    }

    [Fact]
    public void AfterTheThreshold_ContinuedFailureRaisesNothingFurther()
    {
        var (store, _, fanout) = Build(out var subscriber);
        using var _fanout = fanout;
        subscriber.ThrowOnRefresh = true;

        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold * 4; i++)
        {
            store.Publish();
        }

        // The latch. A level implementation raises it on every pass from the fifth onwards, and
        // each raise books the next pass — the self-sustaining loop of S3 8.4.
        var call = Assert.Single(store.ConditionCalls);
        Assert.True(call.Active);
    }

    [Fact]
    public void OneSuccess_ClearsTheConditionOnTheOtherBoundary()
    {
        var (store, _, fanout) = Build(out var subscriber);
        using var _fanout = fanout;
        subscriber.ThrowOnRefresh = true;

        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold; i++)
        {
            store.Publish();
        }

        subscriber.ThrowOnRefresh = false;
        store.Publish();

        Assert.Collection(
            store.ConditionCalls,
            raised => Assert.True(raised.Active),
            cleared => Assert.False(cleared.Active));
    }

    [Fact]
    public void ContinuedSuccess_DoesNotClearTheConditionAgain()
    {
        var (store, _, fanout) = Build(out var subscriber);
        using var _fanout = fanout;
        subscriber.ThrowOnRefresh = true;

        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold; i++)
        {
            store.Publish();
        }

        subscriber.ThrowOnRefresh = false;
        for (var i = 0; i < 5; i++)
        {
            store.Publish();
        }

        Assert.Equal(2, store.ConditionCalls.Count);
    }

    [Fact]
    public void FailureCounter_ResetsOnSuccess_SoTheThresholdIsConsecutive()
    {
        var (store, _, fanout) = Build(out var subscriber);
        using var _fanout = fanout;

        // Four failures, one success, four more failures: never five in a row, so never reported.
        for (var round = 0; round < 2; round++)
        {
            subscriber.ThrowOnRefresh = true;
            for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold - 1; i++)
            {
                store.Publish();
            }

            subscriber.ThrowOnRefresh = false;
            store.Publish();
        }

        Assert.Empty(store.ConditionCalls);
    }

    [Fact]
    public void TheConditionIsNotCleared_WhileAnotherSubscriberIsStillFailing()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        using var fanout = New(store, dispatcher, ticker);

        var first = new RecordingRefreshable { ThrowOnRefresh = true };
        var second = new RecordingRefreshable { ThrowOnRefresh = true };
        fanout.Attach(first);
        fanout.Attach(second);

        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold; i++)
        {
            store.Publish();
        }

        Assert.Equal(2, store.ConditionCalls.Count);

        first.ThrowOnRefresh = false;
        store.Publish();

        // One flag, two claimants: clearing it because one recovered would hide the other, which
        // is still failing.
        Assert.Equal(2, store.ConditionCalls.Count);

        second.ThrowOnRefresh = false;
        store.Publish();

        Assert.Equal(3, store.ConditionCalls.Count);
        Assert.False(store.ConditionCalls[^1].Active);
    }

    private static (FakeStore Store, SynchronousUiDispatcher Dispatcher, SnapshotFanout Fanout) Build(
        out RecordingRefreshable subscriber)
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        var fanout = New(store, dispatcher, ticker);
        subscriber = new RecordingRefreshable();
        fanout.Attach(subscriber);
        return (store, dispatcher, fanout);
    }

    private static SnapshotFanout New(FakeStore store, IUiDispatcher dispatcher, ManualUiTicker ticker)
        => new(
            store,
            dispatcher,
            ticker,
            store,
            store,
            new FakeTimeProvider(Start),
            new RecordingLogger<SnapshotFanout>());
}
