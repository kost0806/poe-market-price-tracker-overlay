using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Tests.TestSupport;
using Xunit;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// S3 8.2 D-PS2 and the measurement behind it (R2) — the merge, and the exception path the
/// measurement did not cover.
/// </summary>
/// <remarks>
/// The original stress run was 8 producers × 20 000 raises × 5 repeats. That is not reproduced
/// here (CI time); what is kept is the assertion that mattered: after the dust settles the UI has
/// seen the newest snapshot, not merely "some" snapshot. Counting passes alone would pass under a
/// broken merge that dropped the last update, so the assertions are on the version observed.
/// </remarks>
public sealed class SnapshotFanoutMergeTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_UnderConcurrentProducers_LosesNoUpdate()
    {
        const int producers = 8;
        const int perProducer = 2_000;

        var store = new FakeStore();
        var dispatcher = new QueueingUiDispatcher();
        var ticker = new ManualUiTicker();
        var subscriber = new RecordingRefreshable();
        using var fanout = Build(store, dispatcher, ticker);
        fanout.Attach(subscriber);

        var pumpDone = false;
        var pump = new Thread(() =>
        {
            while (!Volatile.Read(ref pumpDone))
            {
                dispatcher.Drain();
            }
        });
        pump.Start();

        var threads = new Thread[producers];
        for (var i = 0; i < producers; i++)
        {
            threads[i] = new Thread(() =>
            {
                for (var n = 0; n < perProducer; n++)
                {
                    store.Publish();
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Volatile.Write(ref pumpDone, true);
        pump.Join();
        dispatcher.Drain();

        // The discriminating property: the newest published version reached the UI. A merge that
        // reset its flag after reading the store would settle on a stale version here.
        Assert.Equal(producers * perProducer, store.Current.Version);
        Assert.Equal(store.Current.Version, subscriber.LastVersion);
    }

    [Fact]
    public void Merge_CollapsesABurst_IntoFarFewerPasses()
    {
        var store = new FakeStore();
        var dispatcher = new QueueingUiDispatcher();
        var ticker = new ManualUiTicker();
        var subscriber = new RecordingRefreshable();
        using var fanout = Build(store, dispatcher, ticker);
        fanout.Attach(subscriber);

        for (var i = 0; i < 500; i++)
        {
            store.Publish();
        }

        var ran = dispatcher.Drain();

        // One pending post at a time is the whole contract: 500 raises with no UI thread in
        // between wake it exactly once, and that one pass sees the last version.
        Assert.Equal(1, ran);
        Assert.Equal(500L, subscriber.LastVersion);
    }

    [Fact]
    public void Merge_TickAndSnapshotChange_ShareOneFlag()
    {
        var store = new FakeStore();
        var dispatcher = new QueueingUiDispatcher();
        var ticker = new ManualUiTicker();
        var subscriber = new RecordingRefreshable();
        using var fanout = Build(store, dispatcher, ticker);
        fanout.Attach(subscriber);

        store.Publish();
        ticker.Fire();

        Assert.Equal(1, dispatcher.Drain());
    }

    [Fact]
    public void Tick_AloneDrivesAPass_WhenNoSnapshotChanges()
    {
        var store = new FakeStore();
        var dispatcher = new QueueingUiDispatcher();
        var ticker = new ManualUiTicker();
        var subscriber = new RecordingRefreshable();
        using var fanout = Build(store, dispatcher, ticker);
        fanout.Attach(subscriber);

        // The paradox D-PS3 exists for: when polling dies the snapshot stops changing, which is
        // exactly when the stall has to become visible.
        ticker.Fire();
        dispatcher.Drain();

        Assert.Single(subscriber.Seen);
    }

    [Fact]
    public void Post_ThatThrows_ReleasesTheFlag_AndTheFanoutStaysAlive()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher { ThrowOnPost = true };
        var ticker = new ManualUiTicker();
        var subscriber = new RecordingRefreshable();
        var logger = new RecordingLogger<SnapshotFanout>();
        using var fanout = Build(store, dispatcher, ticker, logger);
        fanout.Attach(subscriber);

        store.Publish();

        Assert.Empty(subscriber.Seen);
        Assert.NotEmpty(logger.WithCode("FanoutPostFailed"));

        // Deafness is the failure this guards against: a claimed flag that no pass ever lowers
        // swallows every later signal for the rest of the session.
        dispatcher.ThrowOnPost = false;
        store.Publish();

        Assert.Single(subscriber.Seen);
        Assert.Equal(2L, subscriber.LastVersion);
    }

    [Fact]
    public void Schedule_WhenShutdownHasAlreadyStarted_NeverClaimsTheFlag()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher { HasShutdownStarted = true };
        var ticker = new ManualUiTicker();
        var subscriber = new RecordingRefreshable();
        using var fanout = Build(store, dispatcher, ticker);
        fanout.Attach(subscriber);

        store.Publish();
        Assert.Empty(subscriber.Seen);

        // The outer guard only, and that is all this test claims: shutdown is read before the CAS,
        // so nothing was claimed and there is nothing to release. The release path is the race
        // below.
        dispatcher.HasShutdownStarted = false;
        store.Publish();

        Assert.Single(subscriber.Seen);
    }

    [Fact]
    public void Post_SkippedByShutdownAfterTheClaim_ReleasesTheFlag()
    {
        var store = new FakeStore();

        // false at the outer guard, true at the inner one: shutdown begins between the two, which
        // is the only arrangement in which the claim is made and the post is not.
        var dispatcher = new ShutdownRacingDispatcher(false, true);
        var ticker = new ManualUiTicker();
        var subscriber = new RecordingRefreshable();
        using var fanout = Build(store, dispatcher, ticker);
        fanout.Attach(subscriber);

        store.Publish();

        Assert.Empty(subscriber.Seen);
        Assert.Equal(0, dispatcher.PostCount);

        // A claim that no pass ever lowers swallows every later signal for the rest of the session
        // (S3 8.2 M4). The script is spent, so both reads now say shutdown has not started.
        store.Publish();

        Assert.Single(subscriber.Seen);
        Assert.Equal(2L, subscriber.LastVersion);
    }

    [Fact]
    public void Pass_UsesOneNow_ForEverySubscriber()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();

        // A clock that moves on every read, not a FakeTimeProvider that stands still: against a
        // still clock a pass that reads the time once per subscriber is indistinguishable from one
        // that reads it once, and the assertion below would hold under either (verified by
        // mutation). Here a second read produces a second value and the test fails.
        var time = new AdvancingTimeProvider(Start, TimeSpan.FromMinutes(1));
        var first = new RecordingRefreshable();
        var second = new RecordingRefreshable();
        using var fanout = Build(store, dispatcher, ticker, timeProvider: time);
        fanout.Attach(first);
        fanout.Attach(second);

        store.Publish();

        // Rows computed against different clock reads can disagree about whether the rate expired
        // (D-PR7). One read per pass is what makes that impossible.
        Assert.Equal(1, time.Reads);
        Assert.Equal(Start, Assert.Single(first.Seen).Now);
        Assert.Equal(Start, Assert.Single(second.Seen).Now);
    }

    [Fact]
    public void Detach_DuringAPass_StillCompletesThatPass()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        var second = new RecordingRefreshable();
        using var fanout = Build(store, dispatcher, ticker);

        var first = new DetachingRefreshable(() => fanout.Detach(second));
        fanout.Attach(first);
        fanout.Attach(second);

        store.Publish();

        // The pass walks a copy taken at its start (S3 8.0), so the detached view model is
        // refreshed once more and only then disappears.
        Assert.Single(second.Seen);

        store.Publish();
        Assert.Single(second.Seen);
    }

    [Fact]
    public void Dispose_StopsTheFanout()
    {
        var store = new FakeStore();
        var dispatcher = new SynchronousUiDispatcher();
        var ticker = new ManualUiTicker();
        var subscriber = new RecordingRefreshable();
        var fanout = Build(store, dispatcher, ticker);
        fanout.Attach(subscriber);

        fanout.Dispose();
        store.Publish();
        ticker.Fire();

        Assert.Empty(subscriber.Seen);
    }

    private static SnapshotFanout Build(
        FakeStore store,
        IUiDispatcher dispatcher,
        ManualUiTicker ticker,
        RecordingLogger<SnapshotFanout>? logger = null,
        TimeProvider? timeProvider = null)
        => new(
            store,
            dispatcher,
            ticker,
            store,
            store,
            timeProvider ?? new FakeTimeProvider(Start),
            logger ?? new RecordingLogger<SnapshotFanout>());

    private sealed class DetachingRefreshable(Action onRefresh) : IRefreshable
    {
        public void Refresh(Core.Domain.MarketSnapshot snapshot, DateTimeOffset now) => onRefresh();
    }

    /// <summary>
    /// A dispatcher whose shutdown flag follows a script, one entry per read.
    /// </summary>
    /// <remarks>
    /// <c>Schedule</c> reads the flag twice — once before the compare-and-swap and once after — and
    /// only a value that changes between the two puts the code through the claim-without-post path.
    /// A plain settable flag cannot express that: set before the call it stops at the outer guard,
    /// set after it never stops at all. Once the script is spent the flag reads false, which is what
    /// lets the same instance prove the flag was released.
    /// </remarks>
    private sealed class ShutdownRacingDispatcher(params bool[] script) : IUiDispatcher
    {
        private readonly Queue<bool> _script = new(script);

        public int PostCount { get; private set; }

        public bool HasShutdownStarted => _script.Count > 0 && _script.Dequeue();

        public bool CheckAccess() => true;

        public void Post(Action action, UiPostPriority priority = UiPostPriority.Normal)
        {
            ArgumentNullException.ThrowIfNull(action);
            PostCount++;
            action();
        }
    }

    /// <summary>
    /// A clock that moves by a fixed step on every read and counts the reads.
    /// </summary>
    /// <remarks>
    /// The counter is the direct statement of "one pass, one <c>now</c>" (D-PR7); the movement is
    /// the belt to its braces, so that even a test that forgot to assert the count would see two
    /// subscribers disagree.
    /// </remarks>
    private sealed class AdvancingTimeProvider(DateTimeOffset start, TimeSpan step) : TimeProvider
    {
        private int _reads;

        /// <summary>How many times <see cref="GetUtcNow"/> has been called.</summary>
        public int Reads => Volatile.Read(ref _reads);

        public override DateTimeOffset GetUtcNow()
            => start + (step * (Interlocked.Increment(ref _reads) - 1));
    }
}
