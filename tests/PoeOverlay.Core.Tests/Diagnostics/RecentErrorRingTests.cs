using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Diagnostics;
using Xunit;

namespace PoeOverlay.Core.Tests.Diagnostics;

/// <summary>
/// S4 4.4 / S2 9.3 — the ring holds the last 64 warning-or-worse entries, discarding the oldest,
/// and hands out copies.
/// </summary>
public sealed class RecentErrorRingTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 16, 5, 0, 0, TimeSpan.Zero);

    private static LogEntry Numbered(int index)
        => new(Start, LogLevel.Warning, "Polling", "entry-" + index.ToString(CultureInfo.InvariantCulture),
            null, null, null, null, null, null);

    [Fact]
    public void DefaultCapacity_IsSixtyFour()
    {
        Assert.Equal(64, new RecentErrorRing().Capacity);
    }

    [Fact]
    public void Snapshot_BeforeAnyAdd_IsEmpty()
    {
        Assert.Empty(new RecentErrorRing().Snapshot());
    }

    [Fact]
    public void Snapshot_PartiallyFilled_ReturnsWhatWasAddedInOrder()
    {
        var ring = new RecentErrorRing();
        for (var i = 0; i < 3; i++)
        {
            ring.Add(Numbered(i));
        }

        Assert.Equal(
            new[] { "entry-0", "entry-1", "entry-2" },
            ring.Snapshot().Select(e => e.Message).ToArray());
    }

    [Fact]
    public void Add_BeyondCapacity_DiscardsTheOldest()
    {
        var ring = new RecentErrorRing();
        for (var i = 0; i < RecentErrorRing.DefaultCapacity + 6; i++)
        {
            ring.Add(Numbered(i));
        }

        var snapshot = ring.Snapshot();

        Assert.Equal(RecentErrorRing.DefaultCapacity, snapshot.Count);
        Assert.Equal("entry-6", snapshot[0].Message);
        Assert.Equal(
            "entry-" + (RecentErrorRing.DefaultCapacity + 5).ToString(CultureInfo.InvariantCulture),
            snapshot[^1].Message);
    }

    [Fact]
    public void Snapshot_IsACopy_AndDoesNotChangeWhenTheRingDoes()
    {
        var ring = new RecentErrorRing(capacity: 2);
        ring.Add(Numbered(1));

        var snapshot = ring.Snapshot();
        ring.Add(Numbered(2));
        ring.Add(Numbered(3));

        Assert.Single(snapshot);
        Assert.Equal("entry-1", snapshot[0].Message);
    }

    [Fact]
    public void FileLogger_FeedsWarningAndAboveIntoTheRingAndNothingBelow()
    {
        var ring = new RecentErrorRing();
        var sink = new RollingFileSink(
            Path.Combine(Path.GetTempPath(), "poeoverlay-tests-unused"),
            new LogLineFormatter(),
            new FakeTimeProvider(Start));

        using var provider = new FileLoggerProvider(sink, ring, new FakeTimeProvider(Start));
        var logger = provider.CreateLogger("Market");

        logger.LogTrace("trace");
        logger.LogDebug("debug");
        logger.LogInformation("information");
        logger.LogWarning("warning");
        logger.LogError("error");
        logger.LogCritical("critical");

        Assert.Equal(
            new[] { "warning", "error", "critical" },
            ring.Snapshot().Select(e => e.Message).ToArray());

        // Every level still reaches the sink, which is where the file lives.
        Assert.Equal(6, sink.QueuedCount);
    }

    [Fact]
    public void FileLogger_UsesTheModuleScopeWhenPresentAndTheCategoryNameOtherwise()
    {
        var ring = new RecentErrorRing();
        var sink = new RollingFileSink(
            Path.Combine(Path.GetTempPath(), "poeoverlay-tests-unused"),
            new LogLineFormatter(),
            new FakeTimeProvider(Start));

        using var provider = new FileLoggerProvider(sink, ring, new FakeTimeProvider(Start));
        var logger = provider.CreateLogger("PoeOverlay.Core.Market.MarketClient");

        logger.LogWarning("without scope");
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["Module"] = "Market",
            ["League"] = "Standard",
            ["RoundNumber"] = 7,
        }))
        {
            logger.LogWarning("with scope");
        }

        var snapshot = ring.Snapshot();

        Assert.Equal("PoeOverlay.Core.Market.MarketClient", snapshot[0].Module);
        Assert.Null(snapshot[0].League);
        Assert.Null(snapshot[0].RoundNumber);

        Assert.Equal("Market", snapshot[1].Module);
        Assert.Equal("Standard", snapshot[1].League);
        Assert.Equal(7, snapshot[1].RoundNumber);
    }

    [Fact]
    public void FileLogger_RecordsTheExceptionTypeWithoutCarryingTheExceptionObject()
    {
        var ring = new RecentErrorRing();
        var sink = new RollingFileSink(
            Path.Combine(Path.GetTempPath(), "poeoverlay-tests-unused"),
            new LogLineFormatter(),
            new FakeTimeProvider(Start));

        using var provider = new FileLoggerProvider(sink, ring, new FakeTimeProvider(Start));
        provider.CreateLogger("Market").LogWarning(new TimeoutException("too slow"), "fetch failed");

        var entry = Assert.Single(ring.Snapshot());

        Assert.Equal("System.TimeoutException", entry.ExceptionType);
        Assert.Contains("too slow", entry.Message, StringComparison.Ordinal);
    }
}
