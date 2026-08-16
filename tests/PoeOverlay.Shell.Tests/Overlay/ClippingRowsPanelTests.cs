using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PoeOverlay.Overlay;
using Xunit;
using Size = System.Windows.Size;

namespace PoeOverlay.Shell.Tests.Overlay;

/// <summary>
/// The hidden-row count, taken from a real layout pass (HLD D19 — estimating it is forbidden).
/// </summary>
/// <remarks>
/// Runs the WPF layout engine on an STA thread but never shows a window, so it needs no desktop
/// session beyond what the test host already has.
/// </remarks>
public sealed class ClippingRowsPanelTests
{
    [Fact]
    public void EverythingFits_NothingIsHidden()
        => Sta(() =>
        {
            var panel = Build(rows: 4, rowHeight: 20);
            Measure(panel, height: 200);

            Assert.Equal(0, panel.HiddenCount);
            Assert.Equal(80d, panel.DesiredSize.Height);
        });

    [Fact]
    public void RowsBeyondTheBudget_AreCounted()
        => Sta(() =>
        {
            var panel = Build(rows: 10, rowHeight: 20);
            Measure(panel, height: 100);

            Assert.Equal(5, panel.HiddenCount);
            Assert.Equal(100d, panel.DesiredSize.Height);
        });

    [Fact]
    public void TheMarkerHeightIsReservedBeforeAnyRowIsAdmitted()
        => Sta(() =>
        {
            // The "+n more" marker must never be the thing that gets clipped, so its height comes
            // off the budget first (S3 4.4.1).
            var panel = Build(rows: 10, rowHeight: 20);
            panel.ReservedHeight = 20d;
            Measure(panel, height: 100);

            Assert.Equal(6, panel.HiddenCount);
        });

    [Fact]
    public void AnUnboundedBudgetHidesNothing()
        => Sta(() =>
        {
            var panel = Build(rows: 50, rowHeight: 20);
            panel.Measure(new Size(400, double.PositiveInfinity));

            Assert.Equal(0, panel.HiddenCount);
        });

    [Fact]
    public void HiddenRowsAreArrangedToZeroSize()
        => Sta(() =>
        {
            var panel = Build(rows: 4, rowHeight: 20);
            Measure(panel, height: 40);
            panel.Arrange(new Rect(0, 0, 400, 40));

            // The arrange slot, not ActualHeight: an element with an explicit Height keeps
            // reporting that height whatever slot it was given.
            var children = panel.Children.Cast<FrameworkElement>().ToList();
            Assert.Equal(20d, LayoutInformation.GetLayoutSlot(children[0]).Height);
            Assert.Equal(0d, LayoutInformation.GetLayoutSlot(children[3]).Height);
        });

    private static ClippingRowsPanel Build(int rows, double rowHeight)
    {
        var panel = new ClippingRowsPanel();
        for (var i = 0; i < rows; i++)
        {
            _ = panel.Children.Add(new Border { Height = rowHeight, Width = 100 });
        }

        return panel;
    }

    private static void Measure(ClippingRowsPanel panel, double height)
        => panel.Measure(new Size(400, height));

    private static void Sta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
