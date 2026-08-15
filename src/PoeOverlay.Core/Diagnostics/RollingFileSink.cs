using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PoeOverlay.Core.Diagnostics;

/// <summary>
/// The rolling log file (S2 9.2 / S4 4.3).
/// </summary>
/// <remarks>
/// <para>
/// Path <c>{directory}/poeoverlay-{yyyyMMdd}.log</c>, rolled on the date and on a 10 MB size cap
/// (suffixes <c>-2</c>, <c>-3</c>, …), retained for 14 days with a single sweep at startup.
/// Writing is a <see cref="Channel"/> plus a thread-pool consumer — no dedicated thread (S2 3.1).
/// </para>
/// <para>
/// On buffer saturation the oldest entries are dropped and a loss notice is queued as an entry of
/// its own, ignoring the cap (D-DG1). It is not attached to the next entry: a crash immediately
/// after saturation would take the notice with it, at exactly the moment the log matters most.
/// Because the notice is an entry, a saturated <see cref="Enqueue"/> has to make room for two
/// writes, not one — see the invariant stated on <see cref="Enqueue"/>.
/// </para>
/// </remarks>
public sealed class RollingFileSink : IAsyncDisposable
{
    /// <summary>S4 15.4: log buffer cap.</summary>
    public const int BufferCapacity = 10_000;

    /// <summary>S4 15.4: rolling file size cap.</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024;

    /// <summary>S4 15.4: retention period.</summary>
    public const int RetentionDays = 14;

    /// <summary>The <see cref="LogEntry.Code"/> carried by a buffer-overflow loss notice.</summary>
    public const string BufferOverflowCode = "LogBufferOverflow";

    /// <summary>The suppression channel that governs ring exposure of loss notices (S4 14.8).</summary>
    public const string BufferOverflowSuppressionChannel = "diagnostics.bufferOverflow";

    private const string FileNamePrefix = "poeoverlay-";
    private const string FileNameSuffix = ".log";

    private readonly string _directory;
    private readonly ILogLineFormatter _formatter;
    private readonly TimeProvider _timeProvider;
    // SingleReader is false because there are genuinely two readers: the consumer task, and the
    // eviction TryRead in Enqueue, which runs on whichever producer thread saturated the buffer.
    // The alternative — moving eviction into the consumer to keep one reader — was rejected: the
    // cap exists for the case where the consumer is stalled on a failing disk (D-DG1), and a
    // consumer-side eviction cannot run at exactly the moment it is needed. _enqueueGate
    // serialises producers against each other only, never against the consumer.
    private readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly object _enqueueGate = new();
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    private int _queued;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private string? _currentPath;
    private DateOnly _currentDate;
    private Task _consumer = Task.CompletedTask;
    private bool _disposed;

    /// <summary>Creates a sink over <paramref name="directory"/>. Nothing touches disk until <see cref="StartAsync"/>.</summary>
    public RollingFileSink(string directory, ILogLineFormatter formatter, TimeProvider timeProvider)
    {
        _directory = directory;
        _formatter = formatter;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// True once a file operation has failed. S2 9.6: the fact that there is no log is the single
    /// most important thing the user can be told, because most of the quiet-failure defence in
    /// this design rests on "it lands in the log".
    /// </summary>
    public bool LoggingUnavailable { get; private set; }

    /// <summary>Number of entries currently waiting to be written.</summary>
    public int QueuedCount => Volatile.Read(ref _queued);

    /// <summary>Number of entries dropped so far because the buffer was full.</summary>
    public int DroppedCount { get; private set; }

    /// <summary>
    /// Synchronous, never blocks on I/O. Applies the buffer cap.
    /// </summary>
    /// <remarks>
    /// Invariant: the queue never exceeds <see cref="BufferCapacity"/> + 1, whatever the arrival
    /// volume. Room is made for <em>both</em> writes a saturated call performs — the entry and the
    /// loss notice — before either is queued; only the notice sits over the cap, and only until the
    /// next saturated call. Evicting one and writing two (the original shape of this method) leaves
    /// the queue one entry longer after every call once the cap is first reached, which is
    /// unbounded growth during exactly the sustained-failure storm the cap exists for.
    /// </remarks>
    public void Enqueue(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_enqueueGate)
        {
            var saturated = false;

            // Leave one free slot so the incoming entry lands within the cap. In the steady state
            // of a storm this evicts two: the entry admitted by the previous call and the notice
            // that was admitted over the cap.
            while (Volatile.Read(ref _queued) >= BufferCapacity && _channel.Reader.TryRead(out _))
            {
                Interlocked.Decrement(ref _queued);
                DroppedCount++;
                saturated = true;
            }

            Write(entry);

            if (!saturated)
            {
                return;
            }

            // D-DG1 / D2: the loss notice is an entry in its own right, queued immediately and
            // ignoring the cap, at Warning so that it also reaches the recent-error ring.
            Write(new LogEntry(
                _timeProvider.GetUtcNow(),
                LogLevel.Warning,
                "Diagnostics",
                FormattableString.Invariant(
                    $"Log buffer full ({BufferCapacity}); dropped the oldest entries. Total dropped this session: {DroppedCount}."),
                League: null,
                DataEpoch: null,
                RoundNumber: null,
                Category: null,
                Code: BufferOverflowCode,
                ExceptionType: null));
        }

        void Write(LogEntry toWrite)
        {
            if (_channel.Writer.TryWrite(toWrite))
            {
                Interlocked.Increment(ref _queued);
            }
        }
    }

    /// <summary>Sweeps expired files, opens today's file and starts the thread-pool consumer.</summary>
    public Task StartAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        PurgeExpired();
        OpenForToday();

        _consumer = Task.Run(() => ConsumeAsync(CancellationToken.None), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Completes the channel, drains it and flushes through to the device.</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        _channel.Writer.TryComplete();
        await _consumer.ConfigureAwait(false);

        await _fileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _writer?.Flush();
            _stream?.Flush(flushToDisk: true);
        }
#pragma warning disable CA1031 // S2 9.5 row 5: Diagnostics file writes. Result: LoggingUnavailable.
        catch (Exception ex)
        {
            MarkUnavailable(ex);
        }
#pragma warning restore CA1031
        finally
        {
            _fileGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await FlushAsync(CancellationToken.None).ConfigureAwait(false);

        _writer?.Dispose();
        _stream?.Dispose();
        _writer = null;
        _stream = null;
        _fileGate.Dispose();
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _queued);

            await _fileGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                RollIfNeeded();
                _writer?.WriteLine(_formatter.Format(entry));
            }
#pragma warning disable CA1031 // S2 9.5 row 5: Diagnostics file writes. Result: LoggingUnavailable.
            catch (Exception ex)
            {
                MarkUnavailable(ex);
            }
#pragma warning restore CA1031
            finally
            {
                _fileGate.Release();
            }
        }
    }

    private void OpenForToday()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            _currentDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
            _currentPath = NextAvailablePath(_currentDate);
            _stream = new FileStream(_currentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }
#pragma warning disable CA1031 // S2 9.5 row 5: Diagnostics file writes. Result: LoggingUnavailable.
        catch (Exception ex)
        {
            MarkUnavailable(ex);
        }
#pragma warning restore CA1031
    }

    private void RollIfNeeded()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var sizeExceeded = _stream is not null && _stream.Length >= MaxFileBytes;

        if (_stream is not null && today == _currentDate && !sizeExceeded)
        {
            return;
        }

        _writer?.Flush();
        _writer?.Dispose();
        _stream?.Dispose();
        _writer = null;
        _stream = null;
        _currentDate = today;
        OpenForToday();
    }

    private string NextAvailablePath(DateOnly date)
    {
        var stem = FileNamePrefix + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var path = Path.Combine(_directory, stem + FileNameSuffix);

        for (var ordinal = 2; FileExceedsCap(path); ordinal++)
        {
            path = Path.Combine(
                _directory,
                FormattableString.Invariant($"{stem}-{ordinal}{FileNameSuffix}"));
        }

        return path;
    }

    private static bool FileExceedsCap(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length >= MaxFileBytes;
    }

    private void PurgeExpired()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            var cutoff = _timeProvider.GetUtcNow().AddDays(-RetentionDays);
            foreach (var path in Directory.EnumerateFiles(_directory, FileNamePrefix + "*" + FileNameSuffix))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime)
                {
                    File.Delete(path);
                }
            }
        }
#pragma warning disable CA1031 // S2 9.5 row 5: Diagnostics file writes. Result: LoggingUnavailable.
        catch (Exception ex)
        {
            MarkUnavailable(ex);
        }
#pragma warning restore CA1031
    }

    private void MarkUnavailable(Exception ex)
    {
        LoggingUnavailable = true;

        // S2 9.6: the logger itself has failed, so the only remaining channel is the debugger.
        Debug.WriteLine(FormattableString.Invariant(
            $"[PoeOverlay] logging unavailable: {ex.GetType().FullName}: {ex.Message}"));
    }

    /// <summary>The file currently being written, or null when the sink never opened one.</summary>
    public string? CurrentPath => _currentPath;
}
