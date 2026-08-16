using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Domain.Ports;

namespace PoeOverlay.Core.Settings;

/// <summary>
/// Owns the settings value, its file, and the three reasons the file may be unwritable
/// (S2 8 / S4 10.5).
/// </summary>
/// <remarks>
/// It reaches the overlay banner through <see cref="IConditionSink"/> and <see cref="IErrorSink"/>
/// rather than through the store directly: S2 1.2 forbids <c>Settings → Store</c>, and S2 2.11
/// requires the settings conditions to appear on the banner, so the dependency is inverted through
/// two Domain ports (D-C5). This type does not know the store's concrete type exists.
/// </remarks>
public sealed partial class SettingsStore : ISettingsSource, IHostedLifecycleService
{
    /// <summary>The one literal <see cref="SettingsLoadResult.Defaulted.ReasonCode"/> can take (S4 10.4).</summary>
    public const string NoFileReason = "NoFile";

    /// <summary>S2 8.5 — the live file.</summary>
    public const string FileName = "settings.json";

    /// <summary>S2 8.5 — the backup <c>File.Replace</c> leaves behind.</summary>
    public const string BackupFileName = "settings.bak.json";

    /// <summary>S2 8.5 — the temporary file the atomic write builds first.</summary>
    public const string TempFileName = "settings.json.tmp";

    /// <summary>S4 15.5 — the breadcrumb a failed shutdown flush leaves for the next start-up.</summary>
    public const string FlushFailureTraceFileName = "settings.flush-failure.trace";

    /// <summary>S4 15.5 — the debounce window for <see cref="Update"/>.</summary>
    public static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(1);

    /// <summary>S4 14.8 — the session-once channel for edits attempted while writes are blocked.</summary>
    private const string WriteBlockedChannel = "settings.writeBlocked";

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly IConditionSink _conditionSink;
    private readonly IErrorSink _errorSink;
    private readonly ILogger<SettingsStore> _logger;
    private readonly ITimer _debounceTimer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _gate = new();
    private readonly HashSet<string> _reportedChannels = new(StringComparer.Ordinal);

    private AppSettings _current = AppSettings.Default;
    private AppSettings? _pending;
    private WriteBlockReason _blockReason;
    private int _writeCount;
    private bool _disposed;

    /// <summary>
    /// Creates the store over <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">
    /// The folder holding <c>settings.json</c>. Composition assembles the <c>%APPDATA%\PoeOverlay</c>
    /// path; tests pass a temporary folder, which is the whole reason this is a parameter.
    /// </param>
    public SettingsStore(
        string directory,
        TimeProvider timeProvider,
        IConditionSink conditionSink,
        IErrorSink errorSink,
        ILogger<SettingsStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(conditionSink);
        ArgumentNullException.ThrowIfNull(errorSink);
        ArgumentNullException.ThrowIfNull(logger);

        _directory = directory;
        _timeProvider = timeProvider;
        _conditionSink = conditionSink;
        _errorSink = errorSink;
        _logger = logger;
        _debounceTimer = timeProvider.CreateTimer(
            _ => OnDebounceElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public event SettingsChangedHandler? Changed;

    /// <inheritdoc />
    public AppSettings Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public WriteBlockReason BlockReason
    {
        get
        {
            lock (_gate)
            {
                return _blockReason;
            }
        }
    }

    /// <summary>Full path of the live settings file.</summary>
    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>Full path of the backup left by the last successful write.</summary>
    public string BackupPath => Path.Combine(_directory, BackupFileName);

    /// <summary>Full path of the shutdown flush-failure breadcrumb.</summary>
    public string FlushFailureTracePath => Path.Combine(_directory, FlushFailureTraceFileName);

    /// <summary>The result of the last <see cref="StartingAsync"/>; test surface for corrections.</summary>
    internal SettingsLoadResult? LastLoadResult { get; private set; }

    /// <summary>How many successful atomic writes have happened; asserts debounce coalescing.</summary>
    internal int WriteCount => Volatile.Read(ref _writeCount);

    /// <summary>Reads the file and reconciles the three write-block reasons (S2 8.4).</summary>
    public Task StartingAsync(CancellationToken ct)
    {
        var result = Load(FilePath, _timeProvider);
        LastLoadResult = result;
        ApplyLoadResult(result);
        ReportFlushFailureTrace();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken ct) => FlushOnShutdownAsync(ct);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _disposed = true;
        }

        _debounceTimer.Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Update(AppSettings next)
    {
        ArgumentNullException.ThrowIfNull(next);

        AppSettings previous;
        lock (_gate)
        {
            previous = _current;

            // S2 8.3: the event fires only on a real change. Reference equality here instead of
            // value equality — which is what a plain array or IReadOnlyList would silently give —
            // makes every save look like a change and starts the re-entry loop D-D2 exists to stop.
            if (previous.Equals(next))
            {
                return;
            }

            Volatile.Write(ref _current, next);
            _pending = next;
        }

        // Published before the notification, so a handler reading Current cannot see the old value.
        Changed?.Invoke(previous, next);

        if (BlockReason != WriteBlockReason.None)
        {
            // The in-memory state still moves and subscribers still hear about it; only the disk
            // write is skipped (S2 8.7). Recorded once per session so a blocked session says so
            // exactly once instead of once per keystroke.
            if (ShouldReportOnce(WriteBlockedChannel))
            {
                Log(LogLevel.Warning, "SettingsWriteBlocked", $"Settings changed while writes are blocked ({BlockReason}).");
            }

            return;
        }

        _debounceTimer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AppSettings? pending;
            lock (_gate)
            {
                pending = _pending;
                _pending = null;
            }

            if (pending is null || BlockReason != WriteBlockReason.None)
            {
                return;
            }

            await WriteAtomicAsync(pending, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public void Acknowledge()
    {
        if (BlockReason != WriteBlockReason.Corrupt)
        {
            // Unreadable would have us overwrite a file we never managed to read, which is exactly
            // the user data that blocking writes was protecting; FutureSchema would overwrite a
            // newer file with an older format. Neither is acknowledgeable (S2 8.7 D-SE2).
            Log(
                LogLevel.Warning,
                "AcknowledgeRefused",
                $"Acknowledge() refused: writes are blocked because of {BlockReason}, which the user cannot clear.");
            return;
        }

        SetBlockReason(WriteBlockReason.None);
        _conditionSink.Set(AppConditionKind.SettingsCorrupt, false, null);

        lock (_gate)
        {
            // Clearing the banner without persisting would throw away every edit made while the
            // banner was up — the accident D17 exists to prevent.
            _pending = _current;
        }

        AcknowledgeWrite = FlushAsync(CancellationToken.None);
    }

    /// <summary>The write started by the last <see cref="Acknowledge"/>, so tests can await it.</summary>
    internal Task AcknowledgeWrite { get; private set; } = Task.CompletedTask;

    private void OnDebounceElapsed()
    {
        // Fire and forget by necessity — a timer callback cannot be awaited. FlushAsync owns the
        // write gate, so a caller that flushes concurrently cannot produce a second write.
        _ = FlushQuietlyAsync();
    }

    private async Task FlushQuietlyAsync()
    {
        try
        {
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Control flow, not failure (S2 1.4).
        }
    }

    private async Task FlushOnShutdownAsync(CancellationToken ct)
    {
        await FlushAsync(ct).ConfigureAwait(false);

        if (LastWriteFailed)
        {
            WriteFlushFailureTrace();
        }
    }

    private bool ShouldReportOnce(string channel)
    {
        lock (_reportedChannels)
        {
            return _reportedChannels.Add(channel);
        }
    }

    private void SetBlockReason(WriteBlockReason reason)
    {
        lock (_gate)
        {
            _blockReason = reason;
        }
    }

    private void Log(LogLevel level, string code, string message, Exception? exception = null)
        => _logger.Log(level, new EventId(0, code), exception, "{Message}", message);
}
