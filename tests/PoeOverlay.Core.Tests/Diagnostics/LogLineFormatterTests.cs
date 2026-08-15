using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using Xunit;

namespace PoeOverlay.Core.Tests.Diagnostics;

/// <summary>
/// S4 4.1 (D-DL4) — character-exact assertions on the log line shape:
/// <c>{At:yyyy-MM-ddTHH:mm:ss.fffZ} [{LevelTag}] {Module,-10} {key=value ...}msg="{EscapedMessage}"</c>
/// </summary>
public sealed class LogLineFormatterTests
{
    private static readonly DateTimeOffset At =
        new(2026, 8, 16, 5, 3, 7, 123, TimeSpan.Zero);

    private static LogEntry Entry(
        LogLevel level = LogLevel.Warning,
        string module = "Polling",
        string message = "hello world",
        string? league = null,
        int? dataEpoch = null,
        int? roundNumber = null,
        string? category = null,
        string? code = null,
        string? exceptionType = null)
        => new(At, level, module, message, league, dataEpoch, roundNumber, category, code, exceptionType);

    [Fact]
    public void Format_AllFieldsPresent_MatchesTheFixedShapeCharacterForCharacter()
    {
        var line = new LogLineFormatter().Format(Entry(
            league: "Standard",
            dataEpoch: 3,
            roundNumber: 7,
            category: "Currency",
            code: "EmptyLines",
            exceptionType: "System.TimeoutException"));

        Assert.Equal(
            "2026-08-16T05:03:07.123Z [WRN] Polling    "
            + "league=Standard dataEpoch=3 round=7 category=Currency code=EmptyLines "
            + "exceptionType=System.TimeoutException "
            + "msg=\"hello world\"",
            line);
    }

    [Fact]
    public void Format_NoOptionalFields_EmitsOnlyPrefixAndMessage()
    {
        var line = new LogLineFormatter().Format(Entry(module: "Store"));

        Assert.Equal("2026-08-16T05:03:07.123Z [WRN] Store      msg=\"hello world\"", line);
    }

    [Fact]
    public void Format_ModuleShorterThanTenCharacters_IsLeftAlignedAndPaddedToTen()
    {
        var line = new LogLineFormatter().Format(Entry(module: "Market"));

        // "Market" + 4 pad characters == 10, then the single separator space before msg=.
        Assert.StartsWith("2026-08-16T05:03:07.123Z [WRN] Market     msg=", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ModuleLongerThanTenCharacters_IsNotTruncated()
    {
        var line = new LogLineFormatter().Format(Entry(module: "Diagnostics"));

        Assert.Equal("2026-08-16T05:03:07.123Z [WRN] Diagnostics msg=\"hello world\"", line);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void Format_LevelTag_IsThreeUpperCaseCharacters(LogLevel level, string expectedTag)
    {
        var line = new LogLineFormatter().Format(Entry(level: level));

        Assert.Contains("[" + expectedTag + "]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_NonUtcTimestamp_IsConvertedToUtc()
    {
        var entry = new LogEntry(
            new DateTimeOffset(2026, 8, 16, 14, 3, 7, 123, TimeSpan.FromHours(9)),
            LogLevel.Information,
            "Polling",
            "x",
            null, null, null, null, null, null);

        Assert.StartsWith("2026-08-16T05:03:07.123Z ", new LogLineFormatter().Format(entry), StringComparison.Ordinal);
    }

    [Fact]
    public void Format_MessageWithNewlinesAndQuotes_StaysOnOneLine()
    {
        var line = new LogLineFormatter().Format(Entry(message: "first\r\nsecond \"quoted\""));

        Assert.Equal(
            "2026-08-16T05:03:07.123Z [WRN] Polling    msg=\"first\\r\\nsecond \\\"quoted\\\"\"",
            line);
        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ValueContainingWhitespace_IsQuoted()
    {
        var line = new LogLineFormatter().Format(Entry(league: "Settlers HC"));

        Assert.Contains("league=\"Settlers HC\" ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ValueWithoutWhitespace_IsNotQuoted()
    {
        var line = new LogLineFormatter().Format(Entry(league: "Standard"));

        Assert.Contains("league=Standard ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ValueWithNewline_IsEscapedInsideTheQuotes()
    {
        var line = new LogLineFormatter().Format(Entry(category: "Cur\nrency"));

        Assert.Contains("category=\"Cur\\nrency\" ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PairOrder_IsLeagueDataEpochRoundCategoryCodeExceptionType()
    {
        var line = new LogLineFormatter().Format(Entry(
            league: "L",
            dataEpoch: 1,
            roundNumber: 2,
            category: "C",
            code: "K",
            exceptionType: "E"));

        var order = new[] { "league=", "dataEpoch=", "round=", "category=", "code=", "exceptionType=", "msg=" };
        var positions = order.Select(key => line.IndexOf(key, StringComparison.Ordinal)).ToArray();

        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.OrderBy(p => p).ToArray(), positions);
    }

    [Fact]
    public void Format_NullPairs_AreOmittedEntirely()
    {
        var line = new LogLineFormatter().Format(Entry(code: "OnlyThisOne"));

        Assert.DoesNotContain("league=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("dataEpoch=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("round=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("category=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("exceptionType=", line, StringComparison.Ordinal);
        Assert.Contains("code=OnlyThisOne ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ZeroValuedNumbers_AreEmittedRatherThanTreatedAsAbsent()
    {
        var line = new LogLineFormatter().Format(Entry(dataEpoch: 0, roundNumber: 0));

        Assert.Contains("dataEpoch=0 ", line, StringComparison.Ordinal);
        Assert.Contains("round=0 ", line, StringComparison.Ordinal);
    }
}
