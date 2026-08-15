using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Market;

/// <summary>
/// Process-wide admission control for poe.ninja (S2 5.7 D13 / S4 7.4).
/// </summary>
/// <remarks>
/// <para>
/// It grants slots and nothing else — it never issues HTTP itself. NFR-02 is a constraint on
/// <em>total</em> traffic, so counting per caller would not satisfy it; one gateway holds the
/// concurrency ceiling of two and the 250 ms minimum issue interval for everybody.
/// </para>
/// <para>
/// One logical request holds one slot. Retries happen inside the slot: re-acquiring per attempt
/// produces a deadlock in which a slot holder waits for a slot.
/// </para>
/// <para>
/// Polling has priority (HLD D13), and D-MK3 keeps that from starving the user: a user-initiated
/// waiter that has waited <see cref="StarvationThreshold"/> takes the next slot unconditionally.
/// Four measured pitfalls shape the implementation and each has a remedy in the code below:
/// synchronous continuations running the woken caller's HTTP inside our lock; a cancelled
/// waiter's <c>TrySetResult</c> returning false and leaking the slot forever; a request arriving
/// just after a release loop found an empty queue; and the 250 ms floor being skipped by the
/// admission path while ageing is only ever evaluated on release.
/// </para>
/// </remarks>
public sealed class NinjaGateway
{
    /// <summary>S4 15.3 — at most two requests in flight across the whole process.</summary>
    public const int MaxConcurrency = 2;

    /// <summary>S4 15.3 — minimum spacing between issues, measured from the previous issue.</summary>
    public static readonly TimeSpan MinIssueInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>S4 15.3 D-MK3 — a user-initiated waiter this old takes the next slot.</summary>
    public static readonly TimeSpan StarvationThreshold = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly List<Waiter> _pollingQueue = [];
    private readonly List<Waiter> _userQueue = [];

    private int _active;
    private DateTimeOffset? _lastIssuedAt;
    private bool _issueScheduled;

    /// <summary>Creates the gateway. Every wait is driven by <paramref name="timeProvider"/>.</summary>
    public NinjaGateway(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Requests in flight right now. Test and diagnostic surface.
    /// </summary>
    /// <remarks>
    /// Only tests read this today, which leaves a real hole: if a slot ever leaks — the measured
    /// pitfalls above are all slot-leak shapes — every category times out forever and the symptom is
    /// indistinguishable from a poe.ninja outage. No condition, log line or heartbeat field reports
    /// the difference. The intended consumer is <c>Polling</c> (S3): sample this together with
    /// <see cref="QueuedCount"/> and raise a condition once <c>QueuedCount &gt; 0</c> with
    /// <c>ActiveCount == 0</c> has persisted past a threshold — a state that cannot be reached by
    /// any healthy schedule, since a non-empty queue with no request in flight means nothing is
    /// left to release the slots. Not implemented here: the gateway must not know about conditions.
    /// </remarks>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>
    /// Callers currently waiting for a slot. The other half of the leak signal described on
    /// <see cref="ActiveCount"/>, and read by tests only until <c>Polling</c> samples the pair.
    /// </summary>
    public int QueuedCount
    {
        get
        {
            lock (_gate)
            {
                return _pollingQueue.Count + _userQueue.Count;
            }
        }
    }

    /// <summary>
    /// Acquires a slot, runs <paramref name="send"/> inside it and always returns the slot.
    /// </summary>
    /// <param name="send">The caller's HTTP send, retries included. The gateway knows no HTTP.</param>
    /// <param name="priority">Scheduling class.</param>
    /// <param name="ct">Cancellation while queued removes the waiter; while holding a slot it propagates into <paramref name="send"/>.</param>
    public async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        RequestPriority priority,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(send);

        await AcquireAsync(priority, ct).ConfigureAwait(false);
        try
        {
            return await send(ct).ConfigureAwait(false);
        }
        finally
        {
            Release();
        }
    }

    /// <summary>Waits for a slot. Exposed to tests so slot accounting can be asserted on its own.</summary>
    internal async Task AcquireAsync(RequestPriority priority, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var waiter = new Waiter(priority, _timeProvider.GetUtcNow());
        lock (_gate)
        {
            Queue(priority).Add(waiter);
        }

        using var registration = ct.Register(() => waiter.Completion.TrySetCanceled(ct));

        if (priority == RequestPriority.UserInitiated)
        {
            // Pitfall (iv): evaluating ageing only on release means two 90 s requests holding both
            // slots let a user request wait forever. The timer re-enters the issue path itself.
            _ = AgeAsync(waiter, ct);
        }

        // Pitfall (iii): a release loop that ended on an empty queue will not come back, so the
        // admission path has to try to issue for itself.
        TryIssue();

        await waiter.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>Returns a slot and lets the next waiter in.</summary>
    internal void Release()
    {
        lock (_gate)
        {
            _active--;
        }

        TryIssue();
    }

    private async Task AgeAsync(Waiter waiter, CancellationToken ct)
    {
        try
        {
            await waiter.Completion.Task.WaitAsync(StarvationThreshold, _timeProvider, ct).ConfigureAwait(false);
            return;
        }
        catch (TimeoutException)
        {
            // Aged. Fall through and try to issue: the waiter now outranks the polling queue.
        }
        catch (OperationCanceledException)
        {
            return;
        }

        TryIssue();
    }

    private void TryIssue()
    {
        lock (_gate)
        {
            while (_active < MaxConcurrency)
            {
                var now = _timeProvider.GetUtcNow();
                var waiter = SelectNext(now);
                if (waiter is null)
                {
                    return;
                }

                if (_lastIssuedAt is { } last && now - last < MinIssueInterval)
                {
                    // The floor is checked on every issue path, not only in the release loop.
                    ScheduleIssue(MinIssueInterval - (now - last));
                    return;
                }

                Queue(waiter.Priority).Remove(waiter);

                // Pitfall (ii): an already-cancelled waiter refuses the result. Consuming a slot
                // for it leaks the slot permanently — twice and the gateway is dead, disguised as
                // a network outage by the category cooldown. Move on to the next waiter instead.
                if (!waiter.Completion.TrySetResult(true))
                {
                    continue;
                }

                _active++;
                _lastIssuedAt = now;
            }
        }
    }

    private Waiter? SelectNext(DateTimeOffset now)
    {
        foreach (var candidate in _userQueue)
        {
            if (now - candidate.EnqueuedAt >= StarvationThreshold)
            {
                return candidate;
            }
        }

        if (_pollingQueue.Count > 0)
        {
            return _pollingQueue[0];
        }

        return _userQueue.Count > 0 ? _userQueue[0] : null;
    }

    private void ScheduleIssue(TimeSpan delay)
    {
        if (_issueScheduled)
        {
            return;
        }

        _issueScheduled = true;
        _ = DelayThenIssueAsync(delay);
    }

    private async Task DelayThenIssueAsync(TimeSpan delay)
    {
        await Task.Delay(delay, _timeProvider).ConfigureAwait(false);
        lock (_gate)
        {
            _issueScheduled = false;
        }

        TryIssue();
    }

    private List<Waiter> Queue(RequestPriority priority)
        => priority == RequestPriority.Polling ? _pollingQueue : _userQueue;

    private sealed class Waiter(RequestPriority priority, DateTimeOffset enqueuedAt)
    {
        // Pitfall (i): the default continuation runs on the thread that calls SetResult, so the
        // woken caller's HTTP send would execute inside our lock.
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RequestPriority Priority { get; } = priority;

        public DateTimeOffset EnqueuedAt { get; } = enqueuedAt;
    }
}
