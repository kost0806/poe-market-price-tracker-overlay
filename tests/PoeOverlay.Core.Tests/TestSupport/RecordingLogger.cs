using Microsoft.Extensions.Logging;

namespace PoeOverlay.Core.Tests.TestSupport;

/// <summary>One captured log call.</summary>
/// <param name="Code">The <c>EventId.Name</c>, which is what <c>FileLogger</c> maps to a log line's <c>code=</c>.</param>
internal sealed record LoggedEntry(LogLevel Level, string? Code, string Message, Exception? Exception);

/// <summary>
/// A logger that keeps what it was told.
/// </summary>
/// <remarks>
/// Not a mock: assertions read the captured entries, which are the same observable output the file
/// sink and the recent-error ring receive in production. "It was logged" is a required, specified
/// behaviour for rejections and for post-after-complete, so it is state worth asserting.
/// </remarks>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LoggedEntry> _entries = [];

    public IReadOnlyList<LoggedEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToArray();
            }
        }
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var entry = new LoggedEntry(
            logLevel,
            string.IsNullOrEmpty(eventId.Name) ? null : eventId.Name,
            formatter(state, exception),
            exception);

        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<LoggedEntry> WithCode(string code)
        => Entries.Where(e => string.Equals(e.Code, code, StringComparison.Ordinal)).ToArray();

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
