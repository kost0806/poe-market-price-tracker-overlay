using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Settings;
using StoreService = PoeOverlay.Core.Store.Store;

namespace PoeOverlay.Core.Polling;

/// <summary>
/// The round loop: the only owner of the data epoch, the round generation and the one persistent
/// timer (S2 7 / S4 9).
/// </summary>
/// <remarks>
/// Nothing here restarts itself. A loop that died has a cause, and quietly restarting it hides the
/// cause and lets the same exception overwrite the log; recovery from <c>LoopExited</c> is an
/// application restart (S2 7.9, D-SH2).
/// </remarks>
public sealed partial class PollingService : BackgroundService
{
    private readonly IMarketClient _market;
    private readonly StoreService _store;
    private readonly ISettingsSource _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PollingService> _logger;
    private readonly NinjaGateway? _gateway;
    private readonly GatewayStallMonitor _stallMonitor = new(PollingOptions.GatewayStallThreshold);
    private readonly ITimer _repollTimer;
    private readonly object _roundGate = new();

    private readonly Channel<PollingTriggerKind> _triggers = Channel.CreateUnbounded<PollingTriggerKind>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _roundCts;
    private int _pendingLeagueChangeTrigger;
    private int _dataEpoch;
    private int _roundGeneration;
    private int _roundNumber;
    private string? _lastResolvedLeague;
    private DateTimeOffset? _lastRoundCompletedAt;
    private bool _disposed;

    /// <summary>
    /// Creates the loop.
    /// </summary>
    /// <param name="gateway">
    /// Optional, and read only to sample <c>ActiveCount</c>/<c>QueuedCount</c>. The S4 9.1
    /// constructor has no such parameter, yet the gateway's own documentation names this class as
    /// the intended consumer of those counters; the parameter is optional so that a composition
    /// which does not supply it still builds, and stall detection is simply absent there.
    /// </param>
    public PollingService(
        IMarketClient market,
        StoreService store,
        ISettingsSource settings,
        TimeProvider timeProvider,
        ILogger<PollingService> logger,
        NinjaGateway? gateway = null)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _market = market;
        _store = store;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
        _gateway = gateway;
        _repollTimer = timeProvider.CreateTimer(
            _ => OnRepollTimerElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The epoch stamped onto every commit; rises only when the league changes (INV-7).</summary>
    internal int DataEpoch => Volatile.Read(ref _dataEpoch);

    /// <summary>The cancellation axis. Never reaches the store, so cancellation cannot be read as contamination.</summary>
    internal int RoundGeneration => Volatile.Read(ref _roundGeneration);

    /// <summary>How many rounds have been started, successful or not.</summary>
    internal int RoundNumber => Volatile.Read(ref _roundNumber);

    /// <summary>
    /// Raised once a round has recorded its outcome.
    /// </summary>
    /// <remarks>
    /// Nothing in the application subscribes to this. It exists so a test can wait for a round to
    /// finish rather than for a stretch of wall-clock time: a timing-based wait passes whether or
    /// not the trigger channel actually delivered the round, which is exactly the defect the
    /// trigger-channel regression is there to catch.
    /// </remarks>
    internal event Action<int, RoundOutcome>? RoundCompleted;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _settings.Changed += OnSettingsChanged;

        var timer = new PeriodicTimer(
            Interval(_settings.Current.RefreshIntervalMinutes), _timeProvider);
        _timer = timer;

        Task? pump = null;
        try
        {
            // S4 9.1 B4: the first round runs outside the channel loop. Without it nothing happens
            // until the first tick, five to sixty minutes after launch, while HLD 4.1 ("start →
            // league → first round → render") and "Loading is not an absorbing state" both assume a
            // round has already been attempted by the time the overlay appears.
            await RunRoundAsync(RoundTrigger.Startup, stoppingToken).ConfigureAwait(false);

            pump = PumpAsync(timer, stoppingToken);

            await foreach (var kind in _triggers.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                var trigger = Promote(Coalesce(kind));
                await RunRoundAsync(trigger, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Control flow, not failure (S2 1.4). The exit kind is decided in the finally block.
        }
#pragma warning disable CA1031 // S2 9.5 row 1: the observable results are an Error entry, lastError and RecordLoopExit.
        catch (Exception ex)
        {
            Log(LogLevel.Error, "LoopExited", "The polling loop threw and will not be restarted.", ex);
            _store.Report(new ErrorRecord(
                _timeProvider.GetUtcNow(),
                "Polling",
                "LoopExited",
                "ui.error.generic",
                ex.Message,
                null,
                _lastResolvedLeague,
                RoundNumber,
                ex.GetType().Name));
        }
#pragma warning restore CA1031
        finally
        {
            _settings.Changed -= OnSettingsChanged;
            _triggers.Writer.TryComplete();

            var exitKind = stoppingToken.IsCancellationRequested ? LoopExitKind.Canceled : LoopExitKind.Faulted;

            // Synchronous TryWrite with its return value checked (S2 6.6 / 7.9): after the store's
            // writer is completed a post silently returns false, and this is the one record that
            // explains why nothing will ever update again.
            _store.RecordLoopExit(exitKind);

            if (pump is not null)
            {
                await pump.ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _repollTimer.Dispose();
            _timer?.Dispose();

            lock (_roundGate)
            {
                _roundCts = null;
            }
        }

        base.Dispose();
    }

    private static TimeSpan Interval(int refreshIntervalMinutes)
        => TimeSpan.FromMinutes(refreshIntervalMinutes);

    /// <summary>
    /// Turns ticks into triggers.
    /// </summary>
    /// <remarks>
    /// <c>pendingTick</c> is preserved to avoid creating a task per iteration, and for no other
    /// reason: measured, <c>PeriodicTimer</c> already buffers one missed tick, so dropping the task
    /// would not lose a tick. The first edition believed otherwise and used that wrong reason to
    /// justify the asymmetric timer/semaphore shape that swallowed repolls.
    /// </remarks>
    private async Task PumpAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        Task<bool>? pendingTick = null;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                pendingTick ??= timer.WaitForNextTickAsync(stoppingToken).AsTask();

                if (!await pendingTick.ConfigureAwait(false))
                {
                    return;
                }

                pendingTick = null;
                Post(PollingTriggerKind.Scheduled);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (ObjectDisposedException)
        {
            // The timer was disposed while a tick was awaited; the loop is already leaving.
        }
    }

    private void Post(PollingTriggerKind kind)
    {
        if (!_triggers.Writer.TryWrite(kind))
        {
            // TryWrite returns false once the channel is completed. Ignoring the return value is
            // how a trigger disappears without trace.
            Log(LogLevel.Warning, "TriggerDropped", $"A {kind} trigger could not be queued; the loop is shutting down.");
        }
    }

    /// <summary>Merges every trigger already sitting in the channel into one round.</summary>
    private PollingTriggerKind Coalesce(PollingTriggerKind first)
    {
        var result = first;
        while (_triggers.Reader.TryRead(out var extra))
        {
            // A repoll dominates: it carries an intent the user just expressed, and a scheduled
            // round would satisfy that intent anyway. Not last-write-wins — a tick queued behind
            // the repoll would then take the merged round and the edit would vanish, which is the
            // same silent loss the single trigger channel replaced the semaphore race to prevent.
            if (extra == PollingTriggerKind.Repoll)
            {
                result = PollingTriggerKind.Repoll;
            }
        }

        return result;
    }

    /// <summary>
    /// Widens a transport value to the four-member domain trigger (S4 9.3).
    /// </summary>
    /// <remarks>
    /// The check-and-consume is a single interlocked exchange, not a volatile read followed by a
    /// write: two settings changes racing the dequeue could otherwise both observe the flag and one
    /// league change would be reported as an ordinary repoll.
    /// </remarks>
    private RoundTrigger Promote(PollingTriggerKind kind)
    {
        if (kind == PollingTriggerKind.Scheduled)
        {
            return RoundTrigger.Scheduled;
        }

        return Interlocked.Exchange(ref _pendingLeagueChangeTrigger, 0) == 1
            ? RoundTrigger.LeagueChanged
            : RoundTrigger.Repoll;
    }

    private void CancelRound()
    {
        lock (_roundGate)
        {
            _roundCts?.Cancel();
        }
    }

    private void Log(LogLevel level, string code, string message, Exception? exception = null)
        => _logger.Log(level, new EventId(0, code), exception, "{Message}", message);
}
