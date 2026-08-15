using System.Text;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Localization;

namespace PoeOverlay.Core.Tests.Localization;

/// <summary>
/// A throwaway <c>Localization/</c> directory plus a started <see cref="LocalizationCatalog"/> over
/// it, so every test below drives the real load path (S2 11 common rules).
/// </summary>
internal sealed class LocalizationHarness : IDisposable
{
    private readonly string _directory;

    private LocalizationHarness(string directory)
    {
        _directory = directory;
        Logger = new RecordingLogger<LocalizationCatalog>();
        Suppression = new SessionSuppressionRegistry(Logger);
    }

    public RecordingLogger<LocalizationCatalog> Logger { get; }

    public SessionSuppressionRegistry Suppression { get; }

    /// <summary>Creates an empty directory; add dictionaries with <see cref="WriteDictionary"/>.</summary>
    public static LocalizationHarness Create()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "poeoverlay-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new LocalizationHarness(directory);
    }

    /// <summary>Writes <paramref name="tag"/>.json from a flat key/value map.</summary>
    public void WriteDictionary(string tag, IReadOnlyDictionary<string, string> entries)
    {
        var json = new StringBuilder("{\n");
        var first = true;
        foreach (var (key, value) in entries)
        {
            if (!first)
            {
                json.Append(",\n");
            }

            first = false;
            json.Append("  \"").Append(Escape(key)).Append("\": \"").Append(Escape(value)).Append('"');
        }

        json.Append("\n}\n");
        WriteRaw(tag, json.ToString());
    }

    /// <summary>Writes <paramref name="tag"/>.json verbatim, valid JSON or not.</summary>
    public void WriteRaw(string tag, string contents)
        => File.WriteAllText(Path.Combine(_directory, tag + ".json"), contents, new UTF8Encoding(false));

    /// <summary>Builds the catalog and runs its load phase (D-L1).</summary>
    public LocalizationCatalog Start()
    {
        var catalog = new LocalizationCatalog(_directory, Logger, Suppression);
        catalog.StartingAsync(CancellationToken.None).GetAwaiter().GetResult();
        return catalog;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}

/// <summary>An <see cref="ILogger{TCategoryName}"/> that keeps what it was told.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>Entries at <paramref name="level"/> whose message mentions <paramref name="needle"/>.</summary>
    public int Count(LogLevel level, string needle)
    {
        var count = 0;
        foreach (var (entryLevel, message) in Entries)
        {
            if (entryLevel == level && message.Contains(needle, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}
