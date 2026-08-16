using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Domain.Ports;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Store;
using StoreService = PoeOverlay.Core.Store.Store;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>
/// A stand-in for the Store that keeps the two properties the fan-out depends on: the snapshot is
/// replaced whole, and one accepted command publishes exactly one snapshot and raises exactly one
/// <c>SnapshotChanged</c> (S2 6.3 — no commit merging).
/// </summary>
/// <remarks>
/// Synchronous where the real Store is a consumer loop. That is the harsher arrangement: the
/// self-sustaining loop S3 8.4 warns about is *easier* to build here, because a deferred
/// <c>Set</c> re-enters immediately rather than after a thread hop.
/// </remarks>
internal sealed class FakeStore : IMarketSnapshotSource, IConditionSink, IErrorSink
{
    private readonly List<SinkCall> _conditionCalls = [];
    private readonly List<ErrorRecord> _errors = [];
    private readonly object _publishLock = new();
    private MarketSnapshot _current = StoreService.CreateInitialSnapshot();

    public event EventHandler? SnapshotChanged;

    public MarketSnapshot Current => Volatile.Read(ref _current);

    /// <summary>Every <see cref="IConditionSink.Set"/> call, with the guard state observed at the call.</summary>
    public IReadOnlyList<SinkCall> ConditionCalls
    {
        get
        {
            lock (_conditionCalls)
            {
                return _conditionCalls.ToArray();
            }
        }
    }

    /// <summary>Every reported error.</summary>
    public IReadOnlyList<ErrorRecord> Errors
    {
        get
        {
            lock (_errors)
            {
                return _errors.ToArray();
            }
        }
    }

    /// <summary>Set to make <see cref="Set"/> throw, as a Store without the storage-group member would.</summary>
    public bool ThrowOnSet { get; set; }

    /// <summary>
    /// Replaces the snapshot and raises the signal, as one accepted command does.
    /// </summary>
    /// <remarks>
    /// The write is serialised so that versions stay monotonic under concurrent producers; the
    /// raise is deliberately outside the lock, because the ordering the merge depends on is
    /// "the write happens before the raise", not "no two raises overlap".
    /// </remarks>
    public long Publish()
    {
        long version;
        lock (_publishLock)
        {
            var next = _current with { Version = _current.Version + 1 };
            Volatile.Write(ref _current, next);
            version = next.Version;
        }

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
        return version;
    }

    /// <summary>Replaces the snapshot with <paramref name="snapshot"/> and raises the signal.</summary>
    public void Publish(MarketSnapshot snapshot)
    {
        lock (_publishLock)
        {
            Volatile.Write(ref _current, snapshot with { Version = _current.Version + 1 });
        }

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Set(AppConditionKind kind, bool active, string? detail)
    {
        lock (_conditionCalls)
        {
            _conditionCalls.Add(new SinkCall(kind, active, detail, UiPassGuard.IsInPass));
        }

        if (ThrowOnSet)
        {
            throw new InvalidOperationException("the storage group rejected " + kind);
        }

        Publish();
    }

    public void Report(ErrorRecord error)
    {
        lock (_errors)
        {
            _errors.Add(error);
        }

        Publish();
    }

    /// <param name="InsideGuardedPass">
    /// Whether the re-entrancy guard was raised when the sink was called. Always false is the
    /// contract: diagnostics are buffered during the subscriber loop and flushed after it (S3 8.4).
    /// </param>
    internal sealed record SinkCall(AppConditionKind Kind, bool Active, string? Detail, bool InsideGuardedPass);
}

/// <summary>
/// A dispatcher that runs the posted delegate inline.
/// </summary>
/// <remarks>
/// HLD 3.4 named this stub and its hazard in the same sentence: it turns raise → handler → commit
/// into a recursion. That is precisely why the convergence measurement used it — it is the
/// arrangement in which a level-triggered condition diverges fastest.
/// </remarks>
internal sealed class SynchronousUiDispatcher : IUiDispatcher
{
    /// <summary>Posts refused after this many, so a divergent implementation fails instead of hanging.</summary>
    public int PostBudget { get; set; } = 200;

    /// <summary>How many posts were accepted.</summary>
    public int PostCount { get; private set; }

    /// <summary>True once the budget was hit — i.e. the loop did not converge.</summary>
    public bool BudgetExhausted { get; private set; }

    public bool HasShutdownStarted { get; set; }

    /// <summary>Set to make <see cref="Post"/> throw, exercising the flag-release path.</summary>
    public bool ThrowOnPost { get; set; }

    public bool CheckAccess() => true;

    public void Post(Action action, UiPostPriority priority = UiPostPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (ThrowOnPost)
        {
            throw new InvalidOperationException("the dispatcher refused the post");
        }

        if (PostCount >= PostBudget)
        {
            BudgetExhausted = true;
            return;
        }

        PostCount++;
        action();
    }
}

/// <summary>A dispatcher that queues, so a test can decide when the UI thread runs.</summary>
internal sealed class QueueingUiDispatcher : IUiDispatcher
{
    private readonly object _sync = new();
    private readonly Queue<Action> _queue = new();

    public bool HasShutdownStarted { get; set; }

    public bool CheckAccess() => true;

    public void Post(Action action, UiPostPriority priority = UiPostPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_sync)
        {
            _queue.Enqueue(action);
        }
    }

    /// <summary>Runs everything currently queued, plus anything those actions queue. Returns the count.</summary>
    public int Drain()
    {
        var ran = 0;
        while (true)
        {
            Action next;
            lock (_sync)
            {
                if (_queue.Count == 0)
                {
                    return ran;
                }

                next = _queue.Dequeue();
            }

            next();
            ran++;
        }
    }
}

/// <summary>A ticker driven by hand (S2 10.8).</summary>
internal sealed class ManualUiTicker : IUiTicker
{
    public event EventHandler? Tick;

    public TimeSpan? StartedWith { get; private set; }

    public bool Stopped { get; private set; }

    public void Start(TimeSpan period) => StartedWith = period;

    public void Stop() => Stopped = true;

    /// <summary>Raises one tick.</summary>
    public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
}

/// <summary>A subscriber that records what it saw.</summary>
internal sealed class RecordingRefreshable : IRefreshable
{
    private readonly List<(long Version, DateTimeOffset Now)> _seen = [];

    /// <summary>Set to make every <c>Refresh</c> throw.</summary>
    public bool ThrowOnRefresh { get; set; }

    public IReadOnlyList<(long Version, DateTimeOffset Now)> Seen
    {
        get
        {
            lock (_seen)
            {
                return _seen.ToArray();
            }
        }
    }

    /// <summary>The snapshot version of the last successful refresh, or -1.</summary>
    public long LastVersion
    {
        get
        {
            lock (_seen)
            {
                return _seen.Count == 0 ? -1L : _seen[^1].Version;
            }
        }
    }

    public void Refresh(MarketSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (ThrowOnRefresh)
        {
            throw new InvalidOperationException("this view model is broken");
        }

        lock (_seen)
        {
            _seen.Add((snapshot.Version, now));
        }
    }
}
