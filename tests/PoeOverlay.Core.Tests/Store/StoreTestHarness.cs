using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Store;
using PoeOverlay.Core.Tests.TestSupport;
using StoreService = PoeOverlay.Core.Store.Store;

namespace PoeOverlay.Core.Tests.Store;

/// <summary>Builders for the values the Store commands carry.</summary>
internal static class StoreTestHarness
{
    internal static readonly DateTimeOffset Start = new(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);

    internal const string League = "Allflame";

    internal static readonly DataTag Tag = new(League, 1);

    internal static ItemPrice Price(string id, decimal value, string? apiName = null)
        => new(new ItemId(id), apiName, value, null, "chaos", null, null, null);

    internal static CategorySnapshot Snapshot(
        ExchangeCategory category = ExchangeCategory.Currency,
        string league = League,
        int epoch = 1,
        decimal value = 1m,
        bool validationBypassed = false,
        params ItemPrice[] items)
    {
        var effective = items.Length > 0 ? items : [Price("divine", value, "Divine Orb")];
        return new CategorySnapshot(
            category,
            effective.ToDictionary(p => p.Id),
            value,
            Start,
            league,
            epoch,
            effective.Length,
            default,
            [],
            false,
            0,
            validationBypassed);
    }

    /// <summary>A snapshot whose item map contains <c>default(ItemId)</c> (S2′′′).</summary>
    internal static CategorySnapshot SnapshotWithEmptyItemId()
    {
        var items = new Dictionary<ItemId, ItemPrice>
        {
            [default] = new(default, null, 1m, null, null, null, null, null),
        };

        return new CategorySnapshot(
            ExchangeCategory.Currency, items, 1m, Start, League, 1, 1, default, [], false, 0, false);
    }

    internal static FailureRecord Failure(FailureKind kind = FailureKind.Network, string? code = null)
        => new(kind, code ?? kind.ToString(), Start, null, null, null);

    internal static ErrorRecord Error(string code = "Whatever")
        => new(Start, "Polling", code, "ui.error.generic", null, null, null, null, null);
}

/// <summary>
/// A started store plus the plumbing every store test needs: a fake clock, a capturing logger and a
/// version waiter driven by <c>SnapshotChanged</c>.
/// </summary>
internal sealed class StoreHarness : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0);
    private int _events;
    private bool _stopped;

    private StoreHarness()
    {
        Time = new FakeTimeProvider(StoreTestHarness.Start);
        Logger = new RecordingLogger<StoreService>();
        Store = new StoreService(Time, Logger);
        Store.SnapshotChanged += OnSnapshotChanged;
    }

    public FakeTimeProvider Time { get; }

    public RecordingLogger<StoreService> Logger { get; }

    public StoreService Store { get; }

    public MarketSnapshot Current => Store.Current;

    /// <summary>How many <c>SnapshotChanged</c> signals have been observed.</summary>
    public int Events => Volatile.Read(ref _events);

    public static async Task<StoreHarness> StartAsync()
    {
        var harness = new StoreHarness();
        await harness.Store.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return harness;
    }

    /// <summary>Creates the harness without starting the consumer loop, so commands buffer up.</summary>
    public static StoreHarness Unstarted() => new();

    public Task StartLoopAsync() => Store.StartAsync(CancellationToken.None);

    /// <summary>
    /// Waits until the store has published <paramref name="version"/> snapshots.
    /// </summary>
    /// <remarks>
    /// Signal driven rather than polled. The five-second ceiling is a deadlock guard, not a timing
    /// assumption — nothing under test depends on wall-clock time.
    /// </remarks>
    public async Task WaitForVersionAsync(long version)
    {
        while (Store.Current.Version < version)
        {
            if (!await _signal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                throw new TimeoutException(
                    $"The store stopped at version {Store.Current.Version} while waiting for {version}.");
            }
        }
    }

    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        await Store.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the loop and unsubscribes.
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose: <c>await using</c> would need a <c>ConfigureAwait</c> that CA2007
    /// demands and that would erase the harness's type. Blocking here cannot deadlock — every await
    /// inside the store is configured not to resume on a captured context.
    /// </remarks>
    public void Dispose()
    {
        if (!_stopped)
        {
            _stopped = true;
            Store.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        Store.SnapshotChanged -= OnSnapshotChanged;
        _signal.Dispose();
    }

    private void OnSnapshotChanged(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _events);
        _signal.Release();
    }
}
