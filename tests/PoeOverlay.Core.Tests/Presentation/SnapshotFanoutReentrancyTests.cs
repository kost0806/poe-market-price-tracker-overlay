using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Tests.TestSupport;
using Xunit;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// S3 8.4 D-PS4 and the convergence measurement of S3 8.4 / 10.1 (00-shell-measurements 10.3).
/// </summary>
/// <remarks>
/// The number seven is the point of this file. Written as the design says — the condition raised on
/// the pass where the counter first reaches the threshold, with an "already reported" latch — two
/// permanently failing subscribers settle after exactly seven passes and never move again. Written
/// as a level test, the same two subscribers feed themselves: every pass sets the condition, every
/// set publishes a snapshot, every snapshot books another pass. The measured rate was about
/// 128 600 republishes a second. The assertion below is therefore on the exact count, not on a
/// generous bound: a bound of "at most 200" would pass under an implementation that merely
/// diverges more slowly.
/// </remarks>
public sealed class SnapshotFanoutReentrancyTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EdgeTriggeredCondition_WithTwoFailingSubscribers_SettlesAfterSevenPasses()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        var time = new FakeTimeProvider(Start);
        using var fanout = Build(store, dispatcher, ticker, time);

        fanout.Attach(new RecordingRefreshable { ThrowOnRefresh = true });
        fanout.Attach(new RecordingRefreshable { ThrowOnRefresh = true });

        // Five external publishes carry both counters to the threshold. Everything after that is
        // the system reacting to itself, which is the whole question.
        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold; i++)
        {
            store.Publish();
        }

        Assert.False(dispatcher.BudgetExhausted, "the fan-out did not converge; it re-triggered itself");
        Assert.Equal(7, dispatcher.PostCount);

        // Nothing further, with or without more time passing.
        time.Advance(TimeSpan.FromSeconds(3));
        ticker.Fire();
        Assert.Equal(8, dispatcher.PostCount);
        ticker.Fire();
        Assert.Equal(9, dispatcher.PostCount);

        // Those two extra passes are the ticker's, and neither raised the condition again.
        Assert.Equal(2, store.ConditionCalls.Count);
    }

    [Fact]
    public void Diagnostics_AreNeverCalled_FromInsideTheGuardedPass()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        using var fanout = Build(store, dispatcher, ticker);

        fanout.Attach(new RecordingRefreshable { ThrowOnRefresh = true });

        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold; i++)
        {
            store.Publish();
        }

        var call = Assert.Single(store.ConditionCalls);
        Assert.Equal(AppConditionKind.ViewModelRefreshFailing, call.Kind);
        Assert.True(call.Active);

        // The guarded window is the subscriber loop; the flush happens after it is lowered. Calling
        // the sink from inside would put a Store command — and so another pass — inside the pass.
        Assert.False(call.InsideGuardedPass);
    }

    [Fact]
    public void OneFailingSubscriber_DoesNotStopTheOthers()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        using var fanout = Build(store, dispatcher, ticker);

        var broken = new RecordingRefreshable { ThrowOnRefresh = true };
        var healthy = new RecordingRefreshable();
        fanout.Attach(broken);
        fanout.Attach(healthy);

        store.Publish();

        Assert.Empty(broken.Seen);
        Assert.Single(healthy.Seen);
    }

    [Fact]
    public void DeferredFlush_IsolatesEachDelegate_AndSurfacesTheFailure()
    {
        var store = new FakeStore { ThrowOnSet = true };
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        var logger = new RecordingLogger<SnapshotFanout>();
        using var fanout = Build(store, dispatcher, ticker, logger: logger);

        fanout.Attach(new RecordingRefreshable { ThrowOnRefresh = true });

        for (var i = 0; i < SnapshotFanout.RefreshFailureThreshold; i++)
        {
            store.Publish();
        }

        // A Store that has not learned the storage-group member rejects the Set synchronously. The
        // per-item catch keeps that from becoming an unhandled UI-thread exception under an empty
        // allow-list (D-SH13), and the error sink is the channel that still works.
        Assert.NotEmpty(logger.WithCode("FanoutDeferredFailed"));
        var error = Assert.Single(store.Errors);
        Assert.Equal("FanoutDeferredFailed", error.Code);
    }

    private static SnapshotFanout Build(
        FakeStore store,
        IUiDispatcher dispatcher,
        ManualUiTicker ticker,
        TimeProvider? timeProvider = null,
        RecordingLogger<SnapshotFanout>? logger = null)
        => new(
            store,
            dispatcher,
            ticker,
            store,
            store,
            timeProvider ?? new FakeTimeProvider(Start),
            logger ?? new RecordingLogger<SnapshotFanout>());
}
