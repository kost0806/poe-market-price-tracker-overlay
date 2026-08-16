using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Domain.Ports;
using PoeOverlay.Core.Store;

namespace PoeOverlay.Core.Presentation.Fanout;

/// <summary>
/// The one native subscriber to <c>Store.SnapshotChanged</c>, and the only thing that drives the
/// three view models (S2 10.1, S3 8 / S4 11.2).
/// </summary>
/// <remarks>
/// <para>
/// Two triggers, one path: <c>SnapshotChanged</c> and <see cref="IUiTicker.Tick"/> both merge into
/// a single pending post (D-PS3), so a tick arriving next to a snapshot change wakes the UI once.
/// </para>
/// <para>
/// The merge itself was measured under 8 producer threads × 20 000 raises × 5 repeats with zero
/// lost updates (S3 0 R2), and the ordering that makes it correct — reset the pending flag
/// <em>before</em> reading <c>store.Current</c> — is not to be redesigned. What the measurement did
/// not settle is the exception path, and that is what <see cref="Schedule"/> handles: a post that
/// is skipped or throws must release the flag, or the fan-out goes permanently deaf (S3 8.2 M4).
/// </para>
/// </remarks>
public sealed partial class SnapshotFanout : IDisposable
{
    /// <summary>S4 15.8 — consecutive <c>Refresh</c> failures before the condition is raised.</summary>
    internal const int RefreshFailureThreshold = 5;

    private readonly IMarketSnapshotSource _snapshotSource;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IUiTicker _uiTicker;
    private readonly IConditionSink _conditionSink;
    private readonly IErrorSink _errorSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SnapshotFanout> _logger;

    private readonly object _sync = new();
    private readonly List<Subscription> _subscribers = [];

    private int _postPending;

    /// <summary>Volatile: <c>Schedule</c> reads it on the Store's consumer thread, <c>Dispose</c> writes it on the UI thread.</summary>
    private volatile bool _disposed;

    /// <summary>Wires both triggers. Neither is unsubscribed until <see cref="Dispose"/>.</summary>
    public SnapshotFanout(
        IMarketSnapshotSource snapshotSource,
        IUiDispatcher uiDispatcher,
        IUiTicker uiTicker,
        IConditionSink conditionSink,
        IErrorSink errorSink,
        TimeProvider timeProvider,
        ILogger<SnapshotFanout> logger)
    {
        ArgumentNullException.ThrowIfNull(snapshotSource);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(uiTicker);
        ArgumentNullException.ThrowIfNull(conditionSink);
        ArgumentNullException.ThrowIfNull(errorSink);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _snapshotSource = snapshotSource;
        _uiDispatcher = uiDispatcher;
        _uiTicker = uiTicker;
        _conditionSink = conditionSink;
        _errorSink = errorSink;
        _timeProvider = timeProvider;
        _logger = logger;

        _snapshotSource.SnapshotChanged += OnSnapshotChanged;
        _uiTicker.Tick += OnTick;
    }

    /// <summary>Passes completed since construction. Test observability for the merge contract.</summary>
    internal int PassCount { get; private set; }

    /// <summary>
    /// Adds a subscriber. UI thread only (S3 8.0).
    /// </summary>
    /// <remarks>
    /// The fan-out holds nothing it was not given: this is a dynamic list, not three fixed slots.
    /// <c>OverlayViewModel</c> and <c>TrayViewModel</c> are attached once at start-up and detached
    /// at shutdown; <c>SettingsViewModel</c> comes and goes with its window (S3 3.1, 5.3).
    /// </remarks>
    public void Attach(IRefreshable subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        Debug.Assert(_uiDispatcher.CheckAccess(), "Attach is UI-thread only (S3 8.0).");

        lock (_sync)
        {
            foreach (var existing in _subscribers)
            {
                if (ReferenceEquals(existing.Target, subscriber))
                {
                    return;
                }
            }

            _subscribers.Add(new Subscription(subscriber));
        }
    }

    /// <summary>
    /// Removes a subscriber. UI thread only (S3 8.0).
    /// </summary>
    /// <remarks>
    /// A pass already under way still calls the detached subscriber once, because the pass walks a
    /// copy taken at its start. That is harmless: <c>Refresh</c> is a pure display calculation, and
    /// a result nobody is bound to is simply discarded.
    /// </remarks>
    public void Detach(IRefreshable subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        Debug.Assert(_uiDispatcher.CheckAccess(), "Detach is UI-thread only (S3 8.0).");

        lock (_sync)
        {
            for (var i = 0; i < _subscribers.Count; i++)
            {
                if (ReferenceEquals(_subscribers[i].Target, subscriber))
                {
                    _subscribers.RemoveAt(i);
                    return;
                }
            }
        }
    }

    /// <summary>Unsubscribes both triggers and drops every subscriber. Idempotent (S3 3.3 B5).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _snapshotSource.SnapshotChanged -= OnSnapshotChanged;
        _uiTicker.Tick -= OnTick;

        lock (_sync)
        {
            _subscribers.Clear();
        }
    }

    /// <summary>One attached view model plus its consecutive-failure state (S3 10.1 D-PS10).</summary>
    private sealed class Subscription(IRefreshable target)
    {
        public IRefreshable Target { get; } = target;

        /// <summary>Consecutive failed passes. Reset by one success.</summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>
        /// The "already reported" latch. Without it the threshold test degenerates into a level
        /// trigger, and the condition it raises becomes the input that schedules the next pass.
        /// </summary>
        public bool Reported { get; set; }
    }
}
