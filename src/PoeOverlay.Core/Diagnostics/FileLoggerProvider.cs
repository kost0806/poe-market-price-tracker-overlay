using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PoeOverlay.Core.Diagnostics;

/// <summary>
/// Hands out <see cref="FileLogger"/> instances over one sink and one ring (S4 4.2).
/// </summary>
/// <remarks>
/// The sink and the ring are injected and owned by Composition, so disposing this provider does
/// not dispose them — <see cref="RollingFileSink"/> is <see cref="IAsyncDisposable"/> and its
/// flush belongs to the shutdown sequence.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly RollingFileSink _sink;
    private readonly RecentErrorRing _ring;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    /// <summary>Creates a provider over an already-constructed sink and ring.</summary>
    public FileLoggerProvider(RollingFileSink sink, RecentErrorRing ring, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _sink = sink;
        _ring = ring;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(
            categoryName,
            static (name, provider) => new FileLogger(name, provider._sink, provider._ring, provider._timeProvider),
            this);

    /// <inheritdoc />
    public void Dispose() => _loggers.Clear();
}
