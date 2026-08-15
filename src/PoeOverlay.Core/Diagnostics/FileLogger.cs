using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PoeOverlay.Core.Diagnostics;

/// <summary>
/// The thin layer over <see cref="ILogger"/> that turns log calls into <see cref="LogEntry"/>
/// values (S2 9.1 / S4 4.2).
/// </summary>
/// <remarks>
/// Sitting on <c>Microsoft.Extensions.Logging</c> rather than a bespoke interface is deliberate:
/// hosting, <c>IHttpClientFactory</c> and the resilience pipeline already log through it, and a
/// private abstraction would keep retry logs out of the file — the very path where diagnosis
/// matters most.
/// <para>
/// Every entry goes to the sink; Warning and above additionally go to the recent-error ring.
/// </para>
/// </remarks>
public sealed class FileLogger : ILogger
{
    private const string ModuleKey = "Module";
    private const string LeagueKey = "League";
    private const string DataEpochKey = "DataEpoch";
    private const string RoundNumberKey = "RoundNumber";
    private const string CategoryKey = "Category";
    private const string CodeKey = "Code";

    private static readonly AsyncLocal<ScopeFrame?> CurrentScope = new();

    private readonly string _categoryName;
    private readonly RollingFileSink _sink;
    private readonly RecentErrorRing _ring;
    private readonly TimeProvider _timeProvider;

    internal FileLogger(string categoryName, RollingFileSink sink, RecentErrorRing ring, TimeProvider timeProvider)
    {
        _categoryName = categoryName;
        _sink = sink;
        _ring = ring;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Pushes Module / League / RoundNumber correlation values (S2 9.1). Recognised when the state
    /// is a sequence of key-value pairs.
    /// </summary>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        var frame = new ScopeFrame(CurrentScope.Value, state);
        CurrentScope.Value = frame;
        return frame;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (exception is not null)
        {
            message = string.Concat(message, " | ", DescribeException(exception, logLevel));
        }

        var entry = new LogEntry(
            _timeProvider.GetUtcNow(),
            logLevel,
            ResolveScopeValue(ModuleKey) ?? _categoryName,
            message,
            ResolveScopeValue(LeagueKey),
            ResolveScopeInt(DataEpochKey),
            ResolveScopeInt(RoundNumberKey),
            ResolveScopeValue(CategoryKey),
            string.IsNullOrEmpty(eventId.Name) ? ResolveScopeValue(CodeKey) : eventId.Name,
            exception?.GetType().FullName);

        _sink.Enqueue(entry);

        if (logLevel >= LogLevel.Warning)
        {
            _ring.Add(entry);
        }
    }

    /// <summary>
    /// Type name plus the first line of the message. The stack trace is appended for Error and
    /// above only (S2 9.1).
    /// </summary>
    /// <remarks>
    /// S4 4.1 specifies a separate <c>stack="…"</c> key, but <see cref="LogEntry"/> has no field to
    /// carry a stack trace, so it is folded into the message here and escaped by the formatter.
    /// </remarks>
    private static string DescribeException(Exception exception, LogLevel logLevel)
    {
        var firstLine = FirstLine(exception.Message);
        var described = FormattableString.Invariant($"{exception.GetType().FullName}: {firstLine}");

        if (logLevel < LogLevel.Error || string.IsNullOrEmpty(exception.StackTrace))
        {
            return described;
        }

        return string.Concat(described, " stack=", exception.StackTrace);
    }

    private static string FirstLine(string value)
    {
        var index = value.IndexOfAny(['\r', '\n']);
        return index < 0 ? value : value[..index];
    }

    private static string? ResolveScopeValue(string key)
    {
        for (var frame = CurrentScope.Value; frame is not null; frame = frame.Parent)
        {
            if (frame.TryGet(key, out var value) && value is not null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static int? ResolveScopeInt(string key)
    {
        var text = ResolveScopeValue(key);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private sealed class ScopeFrame : IDisposable
    {
        private readonly object _state;
        private bool _disposed;

        public ScopeFrame(ScopeFrame? parent, object state)
        {
            Parent = parent;
            _state = state;
        }

        public ScopeFrame? Parent { get; }

        public bool TryGet(string key, out object? value)
        {
            if (_state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                    {
                        value = pair.Value;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentScope.Value = Parent;
        }
    }
}
