using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Diagnostics;
using Xunit;

namespace PoeOverlay.Core.Tests.Diagnostics;

/// <summary>
/// S2 9.5 row 5 / S2 9.6 — every diagnostics file operation catches broadly, and the one thing it
/// must do in exchange is raise <see cref="RollingFileSink.LoggingUnavailable"/>: "there is no log"
/// is the single most important thing the user can be told, because the rest of the quiet-failure
/// defence rests on "it lands in the log". The happy path asserting the flag is false proves
/// nothing about the catch blocks; these tests drive them.
/// </summary>
public sealed class RollingFileSinkFailureTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 16, 5, 0, 0, TimeSpan.Zero);

    /// <summary>A path that exists as a regular file, so <c>Directory.CreateDirectory</c> throws.</summary>
    private readonly string _fileInTheWayOfTheDirectory = Path.Combine(
        Path.GetTempPath(),
        "poeoverlay-tests",
        Guid.NewGuid().ToString("N") + ".not-a-directory");

    public RollingFileSinkFailureTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_fileInTheWayOfTheDirectory)!);
        File.WriteAllText(_fileInTheWayOfTheDirectory, "occupied");
    }

    public void Dispose()
    {
        if (File.Exists(_fileInTheWayOfTheDirectory))
        {
            File.Delete(_fileInTheWayOfTheDirectory);
        }
    }

    private RollingFileSink CreateBlockedSink()
        => new(_fileInTheWayOfTheDirectory, new LogLineFormatter(), new FakeTimeProvider(Start));

    [Fact]
    public async Task StartAsync_WhenTodaysFileCannotBeOpened_MarksLoggingUnavailable()
    {
        await using var sink = CreateBlockedSink();

        await sink.StartAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.True(sink.LoggingUnavailable);
        Assert.Null(sink.CurrentPath);
    }

    [Fact]
    public async Task StartAsync_WhenTodaysFileCannotBeOpened_DoesNotThrow()
    {
        await using var sink = CreateBlockedSink();

        // The catch exists so that a broken log never takes the application with it: startup has to
        // survive, and so does every later enqueue and flush.
        var failure = await Record.ExceptionAsync(
            () => sink.StartAsync(CancellationToken.None)).ConfigureAwait(false);

        Assert.Null(failure);
    }

    [Fact]
    public async Task Enqueue_AfterTheOpenFailed_DrainsWithoutThrowingAndStaysUnavailable()
    {
        await using var sink = CreateBlockedSink();
        await sink.StartAsync(CancellationToken.None).ConfigureAwait(false);

        sink.Enqueue(new LogEntry(
            Start,
            LogLevel.Error,
            "Polling",
            "entry after the sink failed to open",
            null,
            null,
            null,
            null,
            null,
            null));

        var failure = await Record.ExceptionAsync(
            () => sink.FlushAsync(CancellationToken.None)).ConfigureAwait(false);

        Assert.Null(failure);
        Assert.True(sink.LoggingUnavailable);

        // Drained rather than stranded: the entry left the buffer even though it had nowhere to go.
        Assert.Equal(0, sink.QueuedCount);
    }

    [Fact]
    public async Task StartAsync_WhenTheDirectoryIsUsable_LeavesLoggingAvailable()
    {
        // The negative control for the three tests above: the flag is not simply always true.
        var directory = Path.Combine(Path.GetTempPath(), "poeoverlay-tests", Guid.NewGuid().ToString("N"));
        var sink = new RollingFileSink(directory, new LogLineFormatter(), new FakeTimeProvider(Start));
        try
        {
            await sink.StartAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.False(sink.LoggingUnavailable);
            Assert.NotNull(sink.CurrentPath);
        }
        finally
        {
            await sink.DisposeAsync().ConfigureAwait(false);

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
