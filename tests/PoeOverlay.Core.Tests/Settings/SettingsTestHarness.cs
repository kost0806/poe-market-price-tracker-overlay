using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Domain.Ports;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Tests.TestSupport;

namespace PoeOverlay.Core.Tests.Settings;

/// <summary>
/// Captures what the settings store pushed through the two Domain ports.
/// </summary>
/// <remarks>
/// Not a mock: the assertions read the recorded conditions and errors, which are the same values
/// the real sink (the store) would turn into banner state.
/// </remarks>
internal sealed class RecordingSink : IConditionSink, IErrorSink
{
    private readonly List<(AppConditionKind Kind, bool Active, string? Detail)> _conditions = [];
    private readonly List<ErrorRecord> _errors = [];

    public IReadOnlyList<(AppConditionKind Kind, bool Active, string? Detail)> Conditions
    {
        get
        {
            lock (_conditions)
            {
                return _conditions.ToArray();
            }
        }
    }

    public IReadOnlyList<ErrorRecord> Errors
    {
        get
        {
            lock (_errors)
            {
                return _errors.ToArray();
            }
        }
    }

    public void Set(AppConditionKind kind, bool active, string? detail)
    {
        lock (_conditions)
        {
            _conditions.Add((kind, active, detail));
        }
    }

    public void Report(ErrorRecord error)
    {
        lock (_errors)
        {
            _errors.Add(error);
        }
    }

    /// <summary>The latest state of one condition, or null when it was never set.</summary>
    public bool? StateOf(AppConditionKind kind)
    {
        foreach (var entry in Conditions.Reverse())
        {
            if (entry.Kind == kind)
            {
                return entry.Active;
            }
        }

        return null;
    }

    public string? DetailOf(AppConditionKind kind)
    {
        foreach (var entry in Conditions.Reverse())
        {
            if (entry.Kind == kind)
            {
                return entry.Detail;
            }
        }

        return null;
    }
}

/// <summary>A settings store over a private temporary directory.</summary>
internal sealed class SettingsHarness : IDisposable
{
    internal static readonly DateTimeOffset Start = new(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);

    private SettingsHarness(string? initialFileContent)
    {
        Directory = Path.Combine(Path.GetTempPath(), "poeoverlay-settings-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        if (initialFileContent is not null)
        {
            File.WriteAllText(Path.Combine(Directory, SettingsStore.FileName), initialFileContent);
        }

        Time = new FakeTimeProvider(Start);
        Sink = new RecordingSink();
        Logger = new RecordingLogger<SettingsStore>();
        Store = new SettingsStore(Directory, Time, Sink, Sink, Logger);
    }

    public string Directory { get; }

    public FakeTimeProvider Time { get; }

    public RecordingSink Sink { get; }

    public RecordingLogger<SettingsStore> Logger { get; }

    public SettingsStore Store { get; }

    public string FilePath => Path.Combine(Directory, SettingsStore.FileName);

    public string BackupPath => Path.Combine(Directory, SettingsStore.BackupFileName);

    public string TempPath => Path.Combine(Directory, SettingsStore.TempFileName);

    public static SettingsHarness Create(string? initialFileContent = null) => new(initialFileContent);

    /// <summary>Creates the harness and runs the load step.</summary>
    public static async Task<SettingsHarness> StartedAsync(string? initialFileContent = null)
    {
        var harness = new SettingsHarness(initialFileContent);
        await harness.Store.StartingAsync(CancellationToken.None).ConfigureAwait(false);
        return harness;
    }

    public string ReadFile() => File.ReadAllText(FilePath);

    public IReadOnlyList<string> QuarantineFiles()
        => System.IO.Directory.GetFiles(Directory, "settings.corrupt-*.json")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Drives the fake clock until <paramref name="task"/> finishes.
    /// </summary>
    /// <remarks>
    /// The retry backoff is scheduled on the injected clock, so a write that is retrying will never
    /// finish unless the clock moves. The short real waits let the retry loop actually run; nothing
    /// asserted anywhere depends on how long they take.
    /// </remarks>
    public async Task AdvanceUntilCompleteAsync(Task task)
    {
        for (var i = 0; i < 100 && !task.IsCompleted; i++)
        {
            Time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.WhenAny(task, Task.Delay(20)).ConfigureAwait(false);
        }

        await task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        Store.StoppedAsync(CancellationToken.None).GetAwaiter().GetResult();

        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover handle in a failing test must not mask the assertion that failed.
        }
    }
}
