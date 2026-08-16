using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Settings;

/// <summary>
/// The atomic write, its retries, and the shutdown breadcrumb (S2 8.5 / 8.6, S4 10.9).
/// </summary>
public sealed partial class SettingsStore
{
    /// <summary>
    /// S4 15.5 — the delays between the initial attempt and each retry.
    /// </summary>
    /// <remarks>
    /// Retries exist because antivirus and indexer sharing violations on this exact file are common
    /// and typically gone tens of milliseconds later. Three intervals means three retries after the
    /// first attempt: S2 8.5 says "three retries" and S4 15.5 lists three delays, and those two
    /// readings agree only at four total attempts.
    /// </remarks>
    internal static readonly TimeSpan[] WriteRetryDelays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
    ];

    private int _writeAttempts;
    private bool _lastWriteFailed;

    /// <summary>True when the most recent write attempt did not reach the disk.</summary>
    internal bool LastWriteFailed => Volatile.Read(ref _lastWriteFailed);

    /// <summary>Total <c>TryWriteOnceAsync</c> calls; asserts the retry count without timing.</summary>
    internal int WriteAttempts => Volatile.Read(ref _writeAttempts);

    /// <summary>
    /// Writes to a temporary file, flushes it to disk, then swaps it in (S2 8.5).
    /// </summary>
    /// <remarks>
    /// <c>File.Replace</c> takes the backup name as an argument, so replace-and-back-up is one
    /// call, in one directory, with no volume constraint. The backup therefore holds "the last
    /// file successfully written", not D17's "last successfully loaded" — and the former is the
    /// better guarantee anyway, because a file that was just written is a file that loads.
    /// </remarks>
    private async Task WriteAtomicAsync(AppSettings settings, CancellationToken ct)
    {
        var dto = SettingsWriteDtoMapper.ToWriteDto(settings);
        var tmpPath = Path.Combine(_directory, TempFileName);

        for (var attempt = 0; attempt <= WriteRetryDelays.Length; attempt++)
        {
            if (await TryWriteOnceAsync(tmpPath, FilePath, BackupPath, dto, ct).ConfigureAwait(false))
            {
                Volatile.Write(ref _lastWriteFailed, false);
                Interlocked.Increment(ref _writeCount);
                _conditionSink.Set(AppConditionKind.SettingsWriteFailed, false, null);
                return;
            }

            if (attempt < WriteRetryDelays.Length)
            {
                await Task.Delay(WriteRetryDelays[attempt], _timeProvider, ct).ConfigureAwait(false);
            }
        }

        Volatile.Write(ref _lastWriteFailed, true);
        TryDeleteTemp(tmpPath);

        // The condition is cleared by a successful write and by nothing else — there is no
        // acknowledge for it, because acknowledging would claim a save that never happened.
        _conditionSink.Set(AppConditionKind.SettingsWriteFailed, true, FilePath);
        _errorSink.Report(new ErrorRecord(
            _timeProvider.GetUtcNow(),
            "Settings",
            "SettingsWriteFailed",
            "ui.error.settingsWriteFailed",
            FilePath,
            null,
            null,
            null,
            null));

        Log(LogLevel.Error, "SettingsWriteFailed", $"Could not write {FilePath} after {WriteRetryDelays.Length + 1} attempts.");
    }

    private async Task<bool> TryWriteOnceAsync(
        string tmpPath,
        string finalPath,
        string backupPath,
        SettingsWriteDto dto,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _writeAttempts);

        try
        {
            Directory.CreateDirectory(_directory);

            var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer
                    .SerializeAsync(stream, dto, SettingsJsonContext.Default.SettingsWriteDto, ct)
                    .ConfigureAwait(false);

                // Not FlushAsync: the point is to reach the platter before the rename, so that a
                // power loss between the two cannot leave a valid rename over empty content.
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(finalPath))
            {
                File.Replace(tmpPath, finalPath, backupPath);
            }
            else
            {
                File.Move(tmpPath, finalPath);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // S2 9.5 row 6: the observable results are the false return and, once exhausted, SettingsWriteFailed.
        catch (Exception ex)
        {
            Log(LogLevel.Warning, "SettingsWriteAttemptFailed", $"Write attempt to {finalPath} failed.", ex);
            return false;
        }
#pragma warning restore CA1031
    }

    private static void TryDeleteTemp(string tmpPath)
    {
        try
        {
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
#pragma warning disable CA1031 // S2 9.5 row 6: leaving the temp file behind is harmless and already reported.
        catch (Exception)
        {
            // The next attempt opens it with FileMode.Create and overwrites whatever is there.
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Leaves a breadcrumb the next start-up reports once and deletes (S2 8.6, D17).
    /// </summary>
    /// <remarks>
    /// S4 15.5 puts this file in the log directory, but S4 10.5's constructor is given exactly one
    /// directory — the settings directory — so that is where it goes. Composition can point both
    /// at the same folder if the intent was literal.
    /// </remarks>
    private void WriteFlushFailureTrace()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(
                FlushFailureTracePath,
                SettingsValidation.FormatTraceInstant(_timeProvider.GetUtcNow()));
        }
#pragma warning disable CA1031 // S2 9.5 row 6: the observable result is a Warning entry; the shutdown must not be blocked.
        catch (Exception ex)
        {
            Log(LogLevel.Warning, "SettingsFlushFailureTrace", "Could not write the flush-failure trace file.", ex);
        }
#pragma warning restore CA1031
    }
}
