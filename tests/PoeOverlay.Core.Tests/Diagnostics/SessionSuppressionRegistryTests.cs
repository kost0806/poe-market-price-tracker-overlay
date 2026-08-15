using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Diagnostics;
using Xunit;

namespace PoeOverlay.Core.Tests.Diagnostics;

/// <summary>
/// S4 4.5 / S2 9.4 (D-DG2), channel literals from S4 14.8 — report once per (channel, key), log
/// once when a channel saturates, and keep totals so that suppression is not concealment.
/// </summary>
/// <remarks>
/// The registry's logger is a real <see cref="FileLogger"/> writing into a real
/// <see cref="RecentErrorRing"/>, so every assertion below is on observable state.
/// </remarks>
public sealed class SessionSuppressionRegistryTests
{
    private const string UnresolvedKeyChannel = "loc.unresolvedKey";
    private const string ItemNameFallbackChannel = "loc.itemNameFallback";
    private const string BufferOverflowChannel = "diagnostics.bufferOverflow";

    private static readonly DateTimeOffset Start = new(2026, 8, 16, 5, 0, 0, TimeSpan.Zero);

    private static SessionSuppressionRegistry Create(out RecentErrorRing ring, int perChannelCapacity = 512)
    {
        ring = new RecentErrorRing();
        var sink = new RollingFileSink(
            Path.Combine(Path.GetTempPath(), "poeoverlay-tests-unused"),
            new LogLineFormatter(),
            new FakeTimeProvider(Start));
        var provider = new FileLoggerProvider(sink, ring, new FakeTimeProvider(Start));

        return new SessionSuppressionRegistry(provider.CreateLogger("Diagnostics"), perChannelCapacity);
    }

    [Fact]
    public void ShouldReport_FirstOccurrence_IsTrueAndEveryRepeatIsFalse()
    {
        var registry = Create(out _);

        Assert.True(registry.ShouldReport(UnresolvedKeyChannel, "en|ui|ui.price.chaos"));
        Assert.False(registry.ShouldReport(UnresolvedKeyChannel, "en|ui|ui.price.chaos"));
        Assert.False(registry.ShouldReport(UnresolvedKeyChannel, "en|ui|ui.price.chaos"));
    }

    [Fact]
    public void ShouldReport_DistinctKeysOnOneChannel_AreEachReportedOnce()
    {
        var registry = Create(out _);

        Assert.True(registry.ShouldReport(UnresolvedKeyChannel, "key-a"));
        Assert.True(registry.ShouldReport(UnresolvedKeyChannel, "key-b"));
        Assert.False(registry.ShouldReport(UnresolvedKeyChannel, "key-a"));
        Assert.Equal(2, registry.DistinctKeyCount(UnresolvedKeyChannel));
    }

    [Fact]
    public void ShouldReport_SameKeyOnDifferentChannels_IsReportedOncePerChannel()
    {
        var registry = Create(out _);

        Assert.True(registry.ShouldReport(UnresolvedKeyChannel, "divine"));
        Assert.True(registry.ShouldReport(ItemNameFallbackChannel, "divine"));
        Assert.True(registry.ShouldReport(BufferOverflowChannel, "divine"));
        Assert.False(registry.ShouldReport(UnresolvedKeyChannel, "divine"));
    }

    [Fact]
    public void ShouldReport_IsOrdinalAndCaseSensitive()
    {
        var registry = Create(out _);

        Assert.True(registry.ShouldReport(UnresolvedKeyChannel, "Divine"));
        Assert.True(registry.ShouldReport(UnresolvedKeyChannel, "divine"));
    }

    [Fact]
    public void DumpTotals_CountsEveryOccurrencePerChannelNotJustTheReportedOnes()
    {
        var registry = Create(out _);

        registry.ShouldReport(UnresolvedKeyChannel, "key-a");
        registry.ShouldReport(UnresolvedKeyChannel, "key-a");
        registry.ShouldReport(UnresolvedKeyChannel, "key-b");
        registry.ShouldReport(ItemNameFallbackChannel, "key-a");

        var totals = registry.DumpTotals();

        Assert.Equal(3, totals[UnresolvedKeyChannel]);
        Assert.Equal(1, totals[ItemNameFallbackChannel]);
        Assert.Equal(2, totals.Count);
    }

    [Fact]
    public void DumpTotals_BeforeAnyCall_IsEmpty()
    {
        Assert.Empty(Create(out _).DumpTotals());
    }

    [Fact]
    public void ShouldReport_ChannelSaturation_StopsTrackingNewKeysAndLogsExactlyOnce()
    {
        var registry = Create(out var ring, perChannelCapacity: 2);

        Assert.True(registry.ShouldReport(UnresolvedKeyChannel, "key-1"));
        Assert.True(registry.ShouldReport(UnresolvedKeyChannel, "key-2"));
        Assert.Empty(ring.Snapshot());

        Assert.False(registry.ShouldReport(UnresolvedKeyChannel, "key-3"));
        Assert.False(registry.ShouldReport(UnresolvedKeyChannel, "key-4"));

        Assert.Equal(2, registry.DistinctKeyCount(UnresolvedKeyChannel));

        var saturation = Assert.Single(ring.Snapshot());
        Assert.Equal(LogLevel.Warning, saturation.Level);
        Assert.Equal("SuppressionChannelSaturated", saturation.Code);
        Assert.Contains(UnresolvedKeyChannel, saturation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldReport_SaturatedChannel_StillCountsOccurrencesInTheTotals()
    {
        var registry = Create(out _, perChannelCapacity: 2);

        registry.ShouldReport(UnresolvedKeyChannel, "key-1");
        registry.ShouldReport(UnresolvedKeyChannel, "key-2");
        registry.ShouldReport(UnresolvedKeyChannel, "key-3");
        registry.ShouldReport(UnresolvedKeyChannel, "key-4");

        Assert.Equal(4, registry.DumpTotals()[UnresolvedKeyChannel]);
    }

    [Fact]
    public void ShouldReport_SaturatedChannel_StillAnswersFalseForKeysItAlreadyKnows()
    {
        var registry = Create(out _, perChannelCapacity: 2);

        registry.ShouldReport(UnresolvedKeyChannel, "key-1");
        registry.ShouldReport(UnresolvedKeyChannel, "key-2");
        registry.ShouldReport(UnresolvedKeyChannel, "key-3");

        Assert.False(registry.ShouldReport(UnresolvedKeyChannel, "key-1"));
    }

    [Fact]
    public void ReportChannelSaturated_CalledDirectlyAndRepeatedly_LogsOncePerChannel()
    {
        var registry = Create(out var ring);

        registry.ReportChannelSaturated(BufferOverflowChannel);
        registry.ReportChannelSaturated(BufferOverflowChannel);
        registry.ReportChannelSaturated(ItemNameFallbackChannel);

        var snapshot = ring.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, e => e.Message.Contains(BufferOverflowChannel, StringComparison.Ordinal));
        Assert.Contains(snapshot, e => e.Message.Contains(ItemNameFallbackChannel, StringComparison.Ordinal));
    }
}
