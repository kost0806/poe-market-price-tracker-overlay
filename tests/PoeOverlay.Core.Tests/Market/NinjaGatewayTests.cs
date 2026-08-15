using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M19–M21 and M20′ — admission control (S2 5.7 D13, D-MK3).
/// </summary>
/// <remarks>
/// Each test is written so that removing the corresponding remedy makes it fail: lifting the
/// concurrency cap issues everything at once, skipping the 250 ms floor on the admission path
/// stamps two issues at the same instant, ignoring <c>TrySetResult</c>'s return value leaks a slot
/// so the next waiter never runs, and evaluating ageing only on release leaves the user behind the
/// whole polling queue.
/// </remarks>
public sealed class NinjaGatewayTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

    /// <summary>One in-flight request whose completion the test controls.</summary>
    private sealed class Slot
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _issuedTicks;

        public DateTimeOffset? IssuedAt
        {
            get
            {
                var ticks = Interlocked.Read(ref _issuedTicks);
                return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        public bool Issued => Interlocked.Read(ref _issuedTicks) != 0;

        public Task<HttpResponseMessage> Run(
            NinjaGateway gateway,
            TimeProvider time,
            RequestPriority priority,
            CancellationToken ct)
            => gateway.SendAsync(
                async _ =>
                {
                    Interlocked.Exchange(ref _issuedTicks, time.GetUtcNow().UtcTicks);
                    await _release.Task.ConfigureAwait(false);
                    return new HttpResponseMessage();
                },
                priority,
                ct);

        public void Complete() => _release.TrySetResult();
    }

    /// <summary>
    /// Advances the fake clock until <paramref name="condition"/> holds.
    /// </summary>
    /// <remarks>
    /// The one-millisecond real waits park the test thread so the thread pool can run the
    /// continuations a grant wakes; a pure <c>Task.Yield</c> spin starves them when the whole suite
    /// runs in parallel. No production timing depends on them — every interval, backoff and timeout
    /// in Market is driven by <c>time</c>.
    /// </remarks>
    private static async Task<bool> WaitUntilAsync(
        FakeTimeProvider time,
        Func<bool> condition,
        TimeSpan step,
        int maxRounds = 100)
    {
        for (var round = 0; round < maxRounds; round++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(1).ConfigureAwait(false);
            if (condition())
            {
                return true;
            }

            time.Advance(step);
        }

        return condition();
    }

    /// <summary>Lets a good deal of fake time pass so that a negative assertion means something.</summary>
    private static async Task SettleAsync(FakeTimeProvider time, TimeSpan step, int rounds)
    {
        for (var round = 0; round < rounds; round++)
        {
            await Task.Delay(1).ConfigureAwait(false);
            time.Advance(step);
        }

        await Task.Delay(5).ConfigureAwait(false);
    }

    private static async Task DrainAsync(FakeTimeProvider time, IEnumerable<Slot> slots, IEnumerable<Task<HttpResponseMessage>> tasks)
    {
        var all = tasks.ToArray();
        foreach (var slot in slots)
        {
            slot.Complete();
        }

        await WaitUntilAsync(time, () => all.All(t => t.IsCompleted), Step).ConfigureAwait(false);
        foreach (var task in all)
        {
            (await task.ConfigureAwait(false)).Dispose();
        }
    }

    [Fact]
    public async Task M19_FourPollingAndOneUser_NeverExceedTwoInFlightAndAreSpacedByTheFloor()
    {
        var time = new FakeTimeProvider(MarketTestHarness.Start);
        var gateway = new NinjaGateway(time);
        var slots = Enumerable.Range(0, 5).Select(_ => new Slot()).ToArray();

        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 4; i++)
        {
            tasks.Add(slots[i].Run(gateway, time, RequestPriority.Polling, CancellationToken.None));
        }

        tasks.Add(slots[4].Run(gateway, time, RequestPriority.UserInitiated, CancellationToken.None));

        Assert.True(
            await WaitUntilAsync(time, () => slots.Count(s => s.Issued) == 2, Step).ConfigureAwait(false),
            "The gateway never filled both of its slots.");

        // Nobody finishes for the next two seconds. Only the ceiling can hold the other three back.
        await SettleAsync(time, Step, rounds: 8).ConfigureAwait(false);
        Assert.Equal(2, slots.Count(s => s.Issued));
        Assert.Equal(2, gateway.ActiveCount);

        foreach (var slot in slots.Where(s => s.Issued).ToArray())
        {
            slot.Complete();
        }

        Assert.True(
            await WaitUntilAsync(time, () => slots.Count(s => s.Issued) == 4, Step).ConfigureAwait(false),
            "The freed slots were not handed on.");

        await DrainAsync(time, slots, tasks).ConfigureAwait(false);

        Assert.Equal(5, slots.Count(s => s.Issued));
        Assert.Equal(0, gateway.ActiveCount);

        var issues = slots.Select(s => s.IssuedAt!.Value).OrderBy(t => t).ToArray();
        for (var i = 1; i < issues.Length; i++)
        {
            Assert.True(
                issues[i] - issues[i - 1] >= NinjaGateway.MinIssueInterval,
                "Two issues were closer together than the 250 ms floor.");
        }
    }

    [Fact]
    public async Task M20_UserRequestAgedTenSeconds_TakesTheNextSlotAheadOfThePollingQueue()
    {
        var time = new FakeTimeProvider(MarketTestHarness.Start);
        var gateway = new NinjaGateway(time);

        var holders = new[] { new Slot(), new Slot() };
        var holderTasks = holders
            .Select(h => h.Run(gateway, time, RequestPriority.Polling, CancellationToken.None))
            .ToArray();

        Assert.True(
            await WaitUntilAsync(time, () => holders.All(h => h.Issued), Step).ConfigureAwait(false),
            "The two long requests never started.");

        // The polling queue is ahead of the user in arrival order.
        var queuedPolling = new[] { new Slot(), new Slot(), new Slot(), new Slot() };
        var queuedTasks = queuedPolling
            .Select(s => s.Run(gateway, time, RequestPriority.Polling, CancellationToken.None))
            .ToList();

        var user = new Slot();
        queuedTasks.Add(user.Run(gateway, time, RequestPriority.UserInitiated, CancellationToken.None));

        // Both slots stay locked across the whole ageing window — the case that an ageing check
        // evaluated only on release cannot see.
        await SettleAsync(time, TimeSpan.FromSeconds(2), rounds: 8).ConfigureAwait(false);
        Assert.DoesNotContain(queuedPolling, s => s.Issued);
        Assert.False(user.Issued);

        holders[0].Complete();

        Assert.True(
            await WaitUntilAsync(time, () => user.Issued, Step).ConfigureAwait(false),
            "The aged user-initiated request did not take the freed slot.");

        // And it went ahead of four polling requests that had been queued first.
        Assert.Equal(0, queuedPolling.Count(s => s.Issued));

        await DrainAsync(time, holders.Concat(queuedPolling).Append(user), holderTasks.Concat(queuedTasks))
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task M20Prime_ACancelledWaiterDoesNotLeakItsSlot()
    {
        var time = new FakeTimeProvider(MarketTestHarness.Start);
        var gateway = new NinjaGateway(time);

        var holders = new[] { new Slot(), new Slot() };
        var holderTasks = holders
            .Select(h => h.Run(gateway, time, RequestPriority.Polling, CancellationToken.None))
            .ToArray();

        Assert.True(
            await WaitUntilAsync(time, () => holders.All(h => h.Issued), Step).ConfigureAwait(false),
            "The two long requests never started.");

        using var cancelled = new CancellationTokenSource();
        var doomed = new Slot();
        var doomedTask = doomed.Run(gateway, time, RequestPriority.Polling, cancelled.Token);

        var next = new Slot();
        var nextTask = next.Run(gateway, time, RequestPriority.Polling, CancellationToken.None);

        await cancelled.CancelAsync().ConfigureAwait(false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doomedTask).ConfigureAwait(false);

        holders[0].Complete();

        // Counting a slot for the waiter that refused it leaks the slot, the release loop stops
        // there, and nextTask waits for ever while the gateway believes it is full.
        Assert.True(
            await WaitUntilAsync(time, () => next.Issued, Step).ConfigureAwait(false),
            "The slot refused by a cancelled waiter was leaked.");
        Assert.False(doomed.Issued);

        await DrainAsync(time, holders.Append(next), holderTasks.Append(nextTask)).ConfigureAwait(false);
    }

    [Fact]
    public async Task M21_CancellingAQueuedRequest_ThrowsRatherThanReturningAFailureValue()
    {
        var time = new FakeTimeProvider(MarketTestHarness.Start);
        var gateway = new NinjaGateway(time);

        var holders = new[] { new Slot(), new Slot() };
        var holderTasks = holders
            .Select(h => h.Run(gateway, time, RequestPriority.Polling, CancellationToken.None))
            .ToArray();

        Assert.True(
            await WaitUntilAsync(time, () => holders.All(h => h.Issued), Step).ConfigureAwait(false),
            "The two long requests never started.");

        using var scope = new CancellationTokenSource();
        var queued = new Slot();
        var queuedTask = queued.Run(gateway, time, RequestPriority.UserInitiated, scope.Token);

        await scope.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedTask).ConfigureAwait(false);
        Assert.False(queued.Issued);

        await DrainAsync(time, holders, holderTasks).ConfigureAwait(false);
    }

    [Fact]
    public async Task M21_AlreadyCancelledToken_PropagatesOutOfTheClientAsControlFlow()
    {
        var handler = new StubHandler(MarketTestHarness.Fixture("currency-measured.json"));
        var client = MarketTestHarness.CreateClient(handler, out _);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchCategoryAsync("Allflame", ExchangeCategory.Currency, RequestPriority.Polling, cancelled.Token))
            .ConfigureAwait(false);

        Assert.Equal(0, handler.Calls);
    }
}
