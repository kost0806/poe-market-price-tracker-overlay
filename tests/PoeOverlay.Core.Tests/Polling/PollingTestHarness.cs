using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Polling;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Tests.TestSupport;
using StoreService = PoeOverlay.Core.Store.Store;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>An in-memory <see cref="ISettingsSource"/> with no file behind it.</summary>
internal sealed class FakeSettingsSource : ISettingsSource
{
    public AppSettings Current { get; private set; } = AppSettings.Default;

    public event SettingsChangedHandler? Changed;

    public WriteBlockReason BlockReason => WriteBlockReason.None;

    public void Update(AppSettings next)
    {
        var previous = Current;
        if (previous.Equals(next))
        {
            return;
        }

        Current = next;
        Changed?.Invoke(previous, next);
    }

    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    public void Acknowledge()
    {
    }
}

/// <summary>
/// A scripted <see cref="IMarketClient"/>.
/// </summary>
/// <remarks>
/// It records what was asked for, which is how the cooldown and repoll tests assert on the request
/// set instead of on a mock's call log.
/// </remarks>
internal sealed class FakeMarketClient : IMarketClient
{
    private readonly Dictionary<ExchangeCategory, int> _callCounts = [];
    private readonly List<ExchangeCategory> _requested = [];
    private readonly List<IReadOnlyList<ExchangeCategory>> _rounds = [];
    private readonly object _gate = new();

    public LeagueList Leagues { get; set; } = new(
        [new LeagueEntry("Allflame", "Allflame")],
        PollingTestHarness.Start,
        LeagueListStatus.Ok,
        null);

    /// <summary>Set to make the league entry point throw, which is how the loop-fault test injects a fault.</summary>
    public Exception? LeagueException { get; set; }

    /// <summary>Category, and how many times that category has already been asked for.</summary>
    public Func<ExchangeCategory, int, MarketResult<CategorySnapshot>> Respond { get; set; }
        = (category, _) => new MarketResult<CategorySnapshot>.Ok(PollingTestHarness.Snapshot(category));

    /// <summary>When set, category fetches wait on it, so a round can be held open.</summary>
    public TaskCompletionSource<bool>? Hold { get; set; }

    /// <summary>
    /// Whether <see cref="Hold"/> keeps the fetch parked even after the round is cancelled.
    /// </summary>
    /// <remarks>
    /// A real request that has been cancelled still has to unwind, and the loop cannot read the
    /// next trigger until it has. Setting this makes that window explicit rather than incidental,
    /// which is what lets a test queue a known sequence of triggers behind one round instead of
    /// racing the cancelled round's continuation.
    /// </remarks>
    public bool HoldIgnoresCancellation { get; set; }

    /// <summary>Completes when a category fetch has actually been entered, so a test never guesses.</summary>
    public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether returned snapshots are stamped with the requested league, as Market does.</summary>
    public bool StampLeague { get; set; } = true;

    public int LeagueCalls { get; private set; }

    public IReadOnlyList<ExchangeCategory> Requested
    {
        get
        {
            lock (_gate)
            {
                return _requested.ToArray();
            }
        }
    }

    /// <summary>The request set of each round, in order.</summary>
    public IReadOnlyList<IReadOnlyList<ExchangeCategory>> Rounds
    {
        get
        {
            lock (_gate)
            {
                return _rounds.ToArray();
            }
        }
    }

    public async Task<MarketResult<CategorySnapshot>> FetchCategoryAsync(
        string league,
        ExchangeCategory category,
        RequestPriority priority,
        CancellationToken ct)
    {
        int index;
        lock (_gate)
        {
            _requested.Add(category);
            _callCounts.TryGetValue(category, out index);
            _callCounts[category] = index + 1;

            if (_rounds.Count > 0)
            {
                ((List<ExchangeCategory>)_rounds[^1]).Add(category);
            }
        }

        Entered.TrySetResult(true);

        if (Hold is { } hold)
        {
            if (HoldIgnoresCancellation)
            {
                await hold.Task.ConfigureAwait(false);
            }
            else
            {
                await hold.Task.WaitAsync(ct).ConfigureAwait(false);
            }
        }

        ct.ThrowIfCancellationRequested();

        var result = Respond(category, index);

        // MarketClient stamps CategorySnapshot.League from its league parameter, so the fake does
        // the same. Turn it off to reproduce a snapshot that belongs to another league.
        return StampLeague && result is MarketResult<CategorySnapshot>.Ok ok
            ? new MarketResult<CategorySnapshot>.Ok(ok.Value with { League = league })
            : result;
    }

    public Task<MarketResult<LeagueList>> FetchLeaguesAsync(RequestPriority priority, CancellationToken ct)
    {
        lock (_gate)
        {
            LeagueCalls++;
            _rounds.Add(new List<ExchangeCategory>());
        }

        if (LeagueException is { } ex)
        {
            return Task.FromException<MarketResult<LeagueList>>(ex);
        }

        ct.ThrowIfCancellationRequested();
        return Task.FromResult<MarketResult<LeagueList>>(new MarketResult<LeagueList>.Ok(Leagues));
    }
}

/// <summary>Builders shared by the Polling tests.</summary>
internal static class PollingTestHarness
{
    internal static readonly DateTimeOffset Start = new(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);

    internal const string League = "Allflame";

    /// <summary>
    /// A snapshot in the shape Market actually produces.
    /// </summary>
    /// <remarks>
    /// <c>DataEpoch</c> is zero on purpose: <c>IMarketClient.FetchCategoryAsync</c> has no epoch
    /// parameter and Market has no other way to learn one, so every snapshot leaves that module
    /// unstamped and Polling must stamp it before committing.
    /// </remarks>
    internal static CategorySnapshot Snapshot(
        ExchangeCategory category,
        decimal median = 10m,
        string league = League,
        DateTimeOffset? fetchedAt = null,
        decimal divineValue = 194.6m)
    {
        var items = new Dictionary<ItemId, ItemPrice>
        {
            [new ItemId("alchemy")] = new(new ItemId("alchemy"), "Orb of Alchemy", median, null, "chaos", null, null, category),
        };

        if (category == ExchangeCategory.Currency)
        {
            items[new ItemId("divine")] =
                new(new ItemId("divine"), "Divine Orb", divineValue, null, "chaos", null, null, category);
        }

        return new CategorySnapshot(
            category,
            items,
            median,
            fetchedAt ?? Start,
            league,
            0,
            items.Count,
            default,
            [],
            false,
            0,
            false);
    }

    /// <summary>A Currency snapshot with no divine line, which is what D8-c rejects.</summary>
    internal static CategorySnapshot CurrencyWithoutDivine(decimal median = 10m)
    {
        var items = new Dictionary<ItemId, ItemPrice>
        {
            [new ItemId("alchemy")] = new(
                new ItemId("alchemy"), "Orb of Alchemy", median, null, "chaos", null, null, ExchangeCategory.Currency),
        };

        return new CategorySnapshot(
            ExchangeCategory.Currency, items, median, Start, League, 0, 1, default, [], false, 0, false);
    }

    internal static MarketResult<CategorySnapshot> Ok(CategorySnapshot snapshot)
        => new MarketResult<CategorySnapshot>.Ok(snapshot);

    internal static MarketResult<CategorySnapshot> Fail(
        FailureKind kind = FailureKind.Network,
        DateTimeOffset? at = null)
        => new MarketResult<CategorySnapshot>.Fail(
            new FailureRecord(kind, kind.ToString(), at ?? Start, null, null, null));

    internal static AppSettings Settings(
        string? league = League,
        int interval = 5,
        params (string Id, ExchangeCategory Category)[] watchlist)
        => AppSettings.Default with
        {
            League = league,
            RefreshIntervalMinutes = interval,
            Watchlist = new EquatableArray<WatchlistEntry>(watchlist.Select(w => new WatchlistEntry(
                new ItemId(w.Id),
                new CategoryRef(w.Category.ToString(), w.Category),
                null))),
        };
}

/// <summary>
/// A started store, a fake clock, a scripted market and a running polling loop.
/// </summary>
/// <remarks>
/// Waiting is signal driven throughout: rounds are awaited through the service's round event and
/// store state through <c>SnapshotChanged</c>. The five-second ceilings are deadlock guards, not
/// timing assumptions — no assertion depends on wall-clock time.
/// </remarks>
internal sealed class PollingHarness : IDisposable
{
    private readonly SemaphoreSlim _snapshotSignal = new(0);
    private readonly SemaphoreSlim _roundSignal = new(0);
    private readonly List<(int Round, RoundOutcome Outcome)> _rounds = [];
    private bool _stopped;

    private PollingHarness(AppSettings settings)
    {
        Time = new FakeTimeProvider(PollingTestHarness.Start);
        StoreLogger = new RecordingLogger<StoreService>();
        Logger = new RecordingLogger<PollingService>();
        Store = new StoreService(Time, StoreLogger);
        Settings = new FakeSettingsSource();
        Settings.Update(settings);
        Market = new FakeMarketClient();
        Gateway = new NinjaGateway(Time);
        Service = new PollingService(Market, Store, Settings, Time, Logger, Gateway);

        Store.SnapshotChanged += OnSnapshotChanged;
        Service.RoundCompleted += OnRoundCompleted;
    }

    public FakeTimeProvider Time { get; }

    public StoreService Store { get; }

    public FakeSettingsSource Settings { get; }

    public FakeMarketClient Market { get; }

    public NinjaGateway Gateway { get; }

    public PollingService Service { get; }

    public RecordingLogger<PollingService> Logger { get; }

    public RecordingLogger<StoreService> StoreLogger { get; }

    public MarketSnapshot Current => Store.Current;

    public IReadOnlyList<(int Round, RoundOutcome Outcome)> Rounds
    {
        get
        {
            lock (_rounds)
            {
                return _rounds.ToArray();
            }
        }
    }

    /// <summary>Creates the harness without starting the loop, so the market script can be set first.</summary>
    public static async Task<PollingHarness> CreateAsync(AppSettings? settings = null)
    {
        var harness = new PollingHarness(settings ?? PollingTestHarness.Settings());
        await harness.Store.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return harness;
    }

    /// <summary>Starts the loop, which immediately runs the start-up round (S4 9.1 B4).</summary>
    public Task StartAsync() => Service.StartAsync(CancellationToken.None);

    public async Task WaitForRoundsAsync(int count)
    {
        while (Rounds.Count < count)
        {
            if (!await _roundSignal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                throw new TimeoutException($"Only {Rounds.Count} rounds completed while waiting for {count}.");
            }
        }
    }

    public async Task WaitForAsync(Func<MarketSnapshot, bool> predicate, string what)
    {
        while (!predicate(Store.Current))
        {
            if (!await _snapshotSignal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                throw new TimeoutException($"The store never reached the state: {what}.");
            }
        }
    }

    /// <summary>Runs one round and waits for every command it issued to be applied.</summary>
    public async Task RunRoundAsync(int roundNumber)
    {
        await WaitForRoundsAsync(roundNumber).ConfigureAwait(false);
        await DrainAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until the store has applied everything queued so far.
    /// </summary>
    /// <remarks>
    /// A sentinel through the same single-consumer channel, because no field of the snapshot can
    /// stand in for it: the heartbeat's completion instant is already set by an earlier round and
    /// the fake clock does not move inside a round, so "a round finished" is not observable as a
    /// change. Waiting on anything weaker lets the next round read a store that has not yet applied
    /// the previous round's failures — which is precisely the ordering the cooldown depends on.
    /// </remarks>
    public async Task DrainAsync()
    {
        var marker = $"drain-{Guid.NewGuid():N}";
        Store.Set(AppConditionKind.TrayUnavailable, false, marker);

        await WaitForAsync(
            s => s.Conditions.TryGetValue(AppConditionKind.TrayUnavailable, out var c)
                && string.Equals(c.Detail, marker, StringComparison.Ordinal),
            "the command queue drained").ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        await Service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await Store.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!_stopped)
        {
            _stopped = true;
            Service.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            Store.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        Service.RoundCompleted -= OnRoundCompleted;
        Store.SnapshotChanged -= OnSnapshotChanged;
        Service.Dispose();
        _snapshotSignal.Dispose();
        _roundSignal.Dispose();
    }

    private void OnSnapshotChanged(object? sender, EventArgs e) => _snapshotSignal.Release();

    private void OnRoundCompleted(int round, RoundOutcome outcome)
    {
        lock (_rounds)
        {
            _rounds.Add((round, outcome));
        }

        _roundSignal.Release();
    }
}
