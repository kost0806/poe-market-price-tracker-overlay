using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PoeOverlay.Core.Diagnostics;

/// <summary>Renders a <see cref="LogEntry"/> as exactly one line (S4 4.1 D-DL4).</summary>
public interface ILogLineFormatter
{
    /// <summary>Formats one entry. The result never contains a newline.</summary>
    string Format(LogEntry entry);
}

/// <summary>
/// The one log line shape (S2 9.1 / S4 4.1 D-DL4):
/// <c>{At:yyyy-MM-ddTHH:mm:ss.fffZ} [{LevelTag}] {Module,-10} {key=value ...}msg="{EscapedMessage}"</c>
/// </summary>
/// <remarks>
/// One entry is one line so that <c>rg</c> and <c>findstr</c> can find it; newlines and quotes are
/// therefore escaped rather than emitted. Only non-null key=value pairs appear, always in the
/// order league, dataEpoch, round, category, code, exceptionType. Values containing whitespace
/// are quoted.
/// </remarks>
public sealed class LogLineFormatter : ILogLineFormatter
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff";
    private const int ModuleWidth = 10;

    /// <inheritdoc />
    public string Format(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var builder = new StringBuilder(160);

        builder.Append(entry.At.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
        builder.Append("Z [");
        builder.Append(LevelTag(entry.Level));
        builder.Append("] ");
        builder.Append((entry.Module ?? string.Empty).PadRight(ModuleWidth));
        builder.Append(' ');

        AppendPair(builder, "league", entry.League);
        AppendPair(builder, "dataEpoch", entry.DataEpoch?.ToString(CultureInfo.InvariantCulture));
        AppendPair(builder, "round", entry.RoundNumber?.ToString(CultureInfo.InvariantCulture));
        AppendPair(builder, "category", entry.Category);
        AppendPair(builder, "code", entry.Code);
        AppendPair(builder, "exceptionType", entry.ExceptionType);

        builder.Append("msg=\"");
        AppendEscaped(builder, entry.Message ?? string.Empty);
        builder.Append('"');

        return builder.ToString();
    }

    /// <summary>Three-character fixed-width upper-case tag for a level (S4 4.1).</summary>
    internal static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        // S4 4.1 maps six levels; LogLevel.None is not one of them and never reaches a written
        // entry (FileLogger.IsEnabled rejects it). Kept total so the switch cannot throw.
        _ => "NON",
    };

    private static void AppendPair(StringBuilder builder, string key, string? value)
    {
        if (value is null)
        {
            return;
        }

        builder.Append(key);
        builder.Append('=');

        var needsQuotes = ContainsWhitespace(value);
        if (needsQuotes)
        {
            builder.Append('"');
        }

        AppendEscaped(builder, value);

        if (needsQuotes)
        {
            builder.Append('"');
        }

        builder.Append(' ');
    }

    private static bool ContainsWhitespace(string value)
    {
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }
}
