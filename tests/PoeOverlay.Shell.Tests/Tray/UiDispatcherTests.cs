using System.Windows.Threading;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Tray;
using Xunit;

namespace PoeOverlay.Shell.Tests.Tray;

/// <summary>
/// The priority mapping (S3 7.2 B2).
/// </summary>
/// <remarks>
/// The whole reason <see cref="UiPostPriority"/> exists is that <c>DispatcherPriority</c> is a
/// <c>net8.0-windows</c> type and Presentation is <c>net8.0</c>. This is the seam, and it is the one
/// part of the adapter a test can reach without a live dispatcher.
/// </remarks>
public sealed class UiDispatcherTests
{
    [Theory]
    [InlineData(UiPostPriority.Normal, DispatcherPriority.Normal)]
    [InlineData(UiPostPriority.Background, DispatcherPriority.Background)]
    [InlineData(UiPostPriority.Render, DispatcherPriority.Render)]
    public void Map_MatchesTheDocumentedTable(UiPostPriority input, DispatcherPriority expected)
        => Assert.Equal(expected, UiDispatcher.Map(input));

    [Fact]
    public void Map_RejectsAnUndeclaredValue()
        => Assert.Throws<ArgumentOutOfRangeException>(() => UiDispatcher.Map((UiPostPriority)99));

    [Fact]
    public void EveryPriorityIsMapped()
    {
        foreach (var value in Enum.GetValues<UiPostPriority>())
        {
            _ = UiDispatcher.Map(value);
        }
    }
}
