using System.Collections.Frozen;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Domain.Ports;

namespace PoeOverlay.Core.Store;

/// <summary>
/// One consumer, many producers: the single owner of application state (S2 6 / S4 8.3).
/// </summary>
/// <remarks>
/// <para>
/// The store owns its consumer loop rather than borrowing Polling's. That is what lets the
/// application survive the polling loop's death — if the loop that applies commands dies with the
/// loop that produces most of them, nothing can record the fact.
/// </para>
/// <para>
/// It is registered <em>before</em> Polling. The host stops services in reverse order, so Polling
/// stops first and its outermost <c>finally</c> gets to write the loop-exit record before the
/// channel closes. That, not first-render latency, is the real reason for the order.
/// </para>
/// </remarks>
public sealed partial class Store : IHostedService, IMarketSnapshotSource, IConditionSink, IErrorSink, ISearchSource
{
    private static readonly FrozenDictionary<ExchangeCategory, CategorySnapshot> NoCategories =
        new Dictionary<ExchangeCategory, CategorySnapshot>().ToFrozenDictionary();

    private static readonly FrozenDictionary<ExchangeCategory, CategoryStatus> NoStatuses =
        new Dictionary<ExchangeCategory, CategoryStatus>().ToFrozenDictionary();

    private static readonly FrozenDictionary<AppConditionKind, ConditionState> NoConditions =
        new Dictionary<AppConditionKind, ConditionState>().ToFrozenDictionary();

    private readonly Channel<StoreCommand> _channel = Channel.CreateUnbounded<StoreCommand>(
        new UnboundedChannelOptions
        {
            // Unbounded: a bounded channel makes TryWrite fail during normal operation.
            SingleReader = true,
            SingleWriter = false,

            // False is load-bearing. True continues the consumer loop on the producer's thread —
            // the UI thread among them — silently breaking the thread placement of S2 3.1.
            AllowSynchronousContinuations = false,
        });

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<Store> _logger;
    private readonly SessionSuppressionRegistry _suppression;

    private MarketSnapshot _current = CreateInitialSnapshot();
    private Task? _consumer;

    /// <summary>Consumer-thread only: how many commits landed in the round now in progress.</summary>
    private int _landedCommitsThisRound;

    /// <summary>Consumer-thread only: the last rejection code, used as the condition's detail.</summary>
    private string? _lastRejectCode;

    /// <summary>Creates a store whose state is the initial, empty snapshot at version 0.</summary>
    /// <param name="suppression">
    /// Once-per-session channels; S2 6.7 D-ST6 needs one for <c>ExtraMatch</c> faults. S4 8.3's
    /// constructor omits it, so it is optional and defaults to a private registry.
    /// </param>
    public Store(TimeProvider timeProvider, ILogger<Store> logger, SessionSuppressionRegistry? suppression = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _timeProvider = timeProvider;
        _logger = logger;
        _suppression = suppression ?? new SessionSuppressionRegistry(logger);
    }

    /// <inheritdoc />
    public event EventHandler? SnapshotChanged;

    /// <inheritdoc />
    public MarketSnapshot Current => Volatile.Read(ref _current);

    /// <summary>The empty state: no data, no league, version 0.</summary>
    public static MarketSnapshot CreateInitialSnapshot()
        => new(
            NoCategories,
            null,
            null,
            null,
            new Heartbeat(null, 0, null, null, false, null, null),
            null,
            NoStatuses,
            new LeagueResolution(LeagueResolutionState.Pending, null, null),
            NoConditions,
            null,
            0,
            0,
            0,
            0);

    /// <summary>
    /// Queues a command. Synchronous, never blocking, and the return value of <c>TryWrite</c> is
    /// checked.
    /// </summary>
    /// <remarks>
    /// After <c>Complete()</c> <c>TryWrite</c> returns false — the first edition assumed an
    /// unbounded channel always accepts. When it refuses, this log line is the command's last
    /// trace, so the refusal is recorded as an Error rather than dropped.
    /// </remarks>
    public void Post(StoreCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_channel.Writer.TryWrite(command))
        {
            Log(
                LogLevel.Error,
                "PostAfterComplete",
                Invariant($"Dropped {command.GetType().Name}: the store command channel is closed."));
        }
    }

    /// <inheritdoc />
    public void Set(AppConditionKind kind, bool active, string? detail)
        => Post(new StoreCommand.SetConditionCmd(kind, active, detail));

    /// <inheritdoc />
    public void Report(ErrorRecord error)
        => Post(new StoreCommand.SetLastErrorCmd(error));

    /// <summary>Starts the consumer loop.</summary>
    public Task StartAsync(CancellationToken ct)
    {
        _consumer ??= ConsumeAsync(ct);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Completes the writer and waits for the loop to drain.
    /// </summary>
    /// <remarks>
    /// <paramref name="ct"/> is a hard timeout only. It is never handed to <c>ReadAllAsync</c>:
    /// with an already-cancelled token that method drains nothing at all — five buffered commands
    /// become zero applied commands — and the last records written by Polling's outermost
    /// <c>finally</c> are exactly what would be lost.
    /// </remarks>
    public async Task StopAsync(CancellationToken ct)
    {
        // TryComplete rather than Complete: a second stop is a host-lifecycle accident, not a
        // reason to throw on the shutdown path.
        _channel.Writer.TryComplete();

        if (_consumer is { } consumer)
        {
            await consumer.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private void Log(LogLevel level, string code, string message, Exception? exception = null)
        => _logger.Log(level, new EventId(0, code), message, exception, static (state, _) => state);

    private static string Invariant(FormattableString text)
        => text.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
