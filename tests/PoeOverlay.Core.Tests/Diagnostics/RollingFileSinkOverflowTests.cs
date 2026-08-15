using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Diagnostics;
using Xunit;

namespace PoeOverlay.Core.Tests.Diagnostics;

/// <summary>
/// S4 4.3 / D-DG1 / D2 — buffer saturation drops the oldest entry and queues a loss notice of its
/// own that ignores the cap. The notice is Warning so that it also reaches the recent-error ring.
/// </summary>
public sealed class RollingFileSinkOverflowTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 16, 5, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "poeoverlay-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RollingFileSink CreateSink(out FakeTimeProvider time)
    {
        time = new FakeTimeProvider(Start);
        return new RollingFileSink(_directory, new LogLineFormatter(), time);
    }

    private static LogEntry Numbered(int index, DateTimeOffset at)
        => new(at, LogLevel.Information, "Polling", "entry-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null, null, null, null, null, null);

    private async Task<string[]> DrainToLinesAsync(RollingFileSink sink)
    {
        await sink.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await sink.FlushAsync(CancellationToken.None).ConfigureAwait(false);

        var path = sink.CurrentPath;
        Assert.NotNull(path);

        // The sink keeps the file open for writing (FileShare.Read), so a reader has to allow
        // the writer's access back or Windows refuses the handle.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);

        return text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Enqueue_BelowCapacity_WritesEveryEntryAndNoLossNotice()
    {
        await using var sink = CreateSink(out var time);

        for (var i = 0; i < 5; i++)
        {
            sink.Enqueue(Numbered(i, time.GetUtcNow()));
        }

        var lines = await DrainToLinesAsync(sink).ConfigureAwait(false);

        Assert.Equal(5, lines.Length);
        Assert.Equal(0, sink.DroppedCount);
        Assert.DoesNotContain(lines, line => line.Contains(RollingFileSink.BufferOverflowCode, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enqueue_OneOverCapacity_DropsTheOldestEntryAndKeepsTheNewest()
    {
        await using var sink = CreateSink(out var time);

        for (var i = 0; i <= RollingFileSink.BufferCapacity; i++)
        {
            sink.Enqueue(Numbered(i, time.GetUtcNow()));
        }

        var lines = await DrainToLinesAsync(sink).ConfigureAwait(false);

        Assert.Equal(1, sink.DroppedCount);
        Assert.DoesNotContain(lines, line => line.EndsWith("msg=\"entry-0\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("msg=\"entry-1\"", StringComparison.Ordinal));
        Assert.Contains(
            lines,
            line => line.EndsWith(
                "msg=\"entry-" + RollingFileSink.BufferCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enqueue_OneOverCapacity_QueuesTheLossNoticeIgnoringTheCap()
    {
        await using var sink = CreateSink(out var time);

        for (var i = 0; i <= RollingFileSink.BufferCapacity; i++)
        {
            sink.Enqueue(Numbered(i, time.GetUtcNow()));
        }

        var lines = await DrainToLinesAsync(sink).ConfigureAwait(false);

        // 10,000 retained entries plus the notice that was admitted over the cap.
        Assert.Equal(RollingFileSink.BufferCapacity + 1, lines.Length);
        Assert.Single(lines, line => line.Contains("code=" + RollingFileSink.BufferOverflowCode + " ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D2_LossNotice_IsLoggedAtWarningSoItReachesTheRecentErrorRing()
    {
        await using var sink = CreateSink(out var time);

        for (var i = 0; i <= RollingFileSink.BufferCapacity; i++)
        {
            sink.Enqueue(Numbered(i, time.GetUtcNow()));
        }

        var lines = await DrainToLinesAsync(sink).ConfigureAwait(false);
        var notice = Assert.Single(
            lines,
            line => line.Contains("code=" + RollingFileSink.BufferOverflowCode + " ", StringComparison.Ordinal));

        Assert.Contains("[WRN]", notice, StringComparison.Ordinal);
    }

    /// <summary>
    /// One notice per saturated call, not per dropped entry (S4 4.3: "detect the cap being
    /// exceeded, drop the oldest, queue one loss notice ignoring the cap"). The counts differ
    /// because a saturated call has to make room for two writes: the first such call evicts one
    /// entry, and every call after it evicts two — the entry and the notice the previous call
    /// admitted over the cap. That is the price of keeping the queue bounded; the running total in
    /// each notice's message reports it.
    /// </summary>
    [Fact]
    public async Task Enqueue_RepeatedOverflow_EmitsOneNoticePerSaturatedCall()
    {
        await using var sink = CreateSink(out var time);

        for (var i = 0; i < RollingFileSink.BufferCapacity + 3; i++)
        {
            sink.Enqueue(Numbered(i, time.GetUtcNow()));
        }

        Assert.Equal(RollingFileSink.BufferCapacity + 1, sink.QueuedCount);

        var lines = await DrainToLinesAsync(sink).ConfigureAwait(false);

        Assert.Equal(1 + 2 + 2, sink.DroppedCount);
        Assert.Equal(
            3,
            lines.Count(line => line.Contains("code=" + RollingFileSink.BufferOverflowCode + " ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// D-DG1's intent, not its letter: the cap exists so that a sustained failure-logging storm
    /// cannot grow memory without bound. The loss notice is admitted over the cap, so the queue may
    /// sit a small constant above <see cref="RollingFileSink.BufferCapacity"/> — but that constant
    /// must not scale with the number of entries that arrived.
    /// </summary>
    [Fact]
    public async Task Enqueue_SustainedOverflow_KeepsTheQueueBoundedRegardlessOfVolume()
    {
        // No StartAsync: with no consumer draining, QueuedCount is the true length of the buffer.
        await using var sink = CreateSink(out var time);

        const int Overshoot = 50_000;
        for (var i = 0; i < RollingFileSink.BufferCapacity + Overshoot; i++)
        {
            sink.Enqueue(Numbered(i, time.GetUtcNow()));
        }

        // Slack of 8 is deliberately larger than the one over-cap slot the notice needs, so the
        // assertion pins "bounded by a small constant" rather than one implementation's exact peak.
        Assert.InRange(sink.QueuedCount, 0, RollingFileSink.BufferCapacity + 8);
    }

    [Fact]
    public async Task StartAsync_WritesToTheDatedFileName()
    {
        await using var sink = CreateSink(out var time);
        sink.Enqueue(Numbered(1, time.GetUtcNow()));

        await DrainToLinesAsync(sink).ConfigureAwait(false);

        Assert.Equal("poeoverlay-20260816.log", Path.GetFileName(sink.CurrentPath));
        Assert.False(sink.LoggingUnavailable);
    }

    [Fact]
    public async Task StartAsync_PurgesFilesOlderThanTheRetentionWindow()
    {
        Directory.CreateDirectory(_directory);
        var stale = Path.Combine(_directory, "poeoverlay-20260701.log");
        var fresh = Path.Combine(_directory, "poeoverlay-20260815.log");
        await File.WriteAllTextAsync(stale, "old").ConfigureAwait(false);
        await File.WriteAllTextAsync(fresh, "new").ConfigureAwait(false);
        File.SetLastWriteTimeUtc(stale, Start.UtcDateTime.AddDays(-(RollingFileSink.RetentionDays + 1)));
        File.SetLastWriteTimeUtc(fresh, Start.UtcDateTime.AddDays(-1));

        await using var sink = CreateSink(out _);
        await sink.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await sink.FlushAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }
}
