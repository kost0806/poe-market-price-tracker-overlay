using System.Windows;
using PoeOverlay.Core.Settings;
using PoeOverlay.Overlay;
using Xunit;
using Size = System.Windows.Size;

namespace PoeOverlay.Shell.Tests.Overlay;

/// <summary>
/// The minimum-visible-area rule (S3 4.5 D-SH8).
/// </summary>
public sealed class OverlayGeometryValidatorTests
{
    private static readonly Rect Primary = new(0, 0, 2560, 1400);
    private static readonly Rect Secondary = new(-1920, 156, 1920, 1030);
    private static readonly Size Footer = new(420, 20);

    [Fact]
    public void FullyContained_IsValid()
    {
        var bounds = new Rect(100, 100, 420, 500);
        Assert.True(OverlayGeometryValidator.HasMinimumVisibleArea(bounds, [Primary], Footer));
    }

    [Fact]
    public void TallerThanTheWorkArea_IsStillValid()
    {
        // The rule HLD 7 originally stated ("fully contained") rejects this, which is the over-strict
        // behaviour D-SH8 relaxes.
        var bounds = new Rect(100, 100, 420, 4000);
        Assert.True(OverlayGeometryValidator.HasMinimumVisibleArea(bounds, [Primary], Footer));
    }

    [Fact]
    public void EntirelyOffScreen_IsRejected()
    {
        var bounds = new Rect(9000, 9000, 420, 500);
        Assert.False(OverlayGeometryValidator.HasMinimumVisibleArea(bounds, [Primary, Secondary], Footer));
    }

    [Fact]
    public void OnlyASliverOfWidthVisible_IsRejected()
    {
        // Ten pixels of the left edge shows neither the attribution nor the update time, which is
        // what the footer-sized threshold is protecting.
        var bounds = new Rect(2550, 100, 420, 500);
        Assert.False(OverlayGeometryValidator.HasMinimumVisibleArea(bounds, [Primary], Footer));
    }

    [Fact]
    public void OnlyASliverOfHeightVisible_IsRejected()
    {
        var bounds = new Rect(100, 1395, 420, 500);
        Assert.False(OverlayGeometryValidator.HasMinimumVisibleArea(bounds, [Primary], Footer));
    }

    [Fact]
    public void SpanningAVerticalSeam_IsRejected()
    {
        // A consequence of the footer being a full-width strip: straddling a left/right monitor
        // boundary leaves neither work area showing a whole footer, so no attribution and no
        // update time is legible on either screen.
        var bounds = new Rect(-200, 300, 420, 500);
        Assert.False(OverlayGeometryValidator.HasMinimumVisibleArea(bounds, [Primary, Secondary], Footer));
    }

    [Fact]
    public void SpanningAHorizontalSeamWithTheFooterOnOne_IsValid()
    {
        // Stacked monitors: the window crosses the seam, but the upper work area still shows a
        // whole footer's worth of it, which is all D-SH8 asks for.
        var stackedBelow = new Rect(0, 1400, 2560, 1030);
        var bounds = new Rect(100, 1300, 420, 500);
        Assert.True(OverlayGeometryValidator.HasMinimumVisibleArea(bounds, [Primary, stackedBelow], Footer));
    }

    [Fact]
    public void BestWorkArea_PicksTheOneShowingMost()
    {
        var bounds = new Rect(-1800, 300, 420, 500);
        var chosen = OverlayGeometryValidator.BestWorkArea(bounds, [Primary, Secondary], Footer);
        Assert.Equal(Secondary, chosen);
    }

    [Fact]
    public void NoWorkAreas_IsRejectedRatherThanThrowing()
        => Assert.False(OverlayGeometryValidator.HasMinimumVisibleArea(new Rect(0, 0, 420, 500), [], Footer));

    [Fact]
    public void ClampToDefault_ReturnsTheHldDefaults()
    {
        var (x, y) = OverlayGeometryValidator.ClampToDefault();
        Assert.Equal(WindowSettings.Default.X, x);
        Assert.Equal(WindowSettings.Default.Y, y);
    }
}
