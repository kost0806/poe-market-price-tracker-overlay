using PoeOverlay.Overlay;
using Xunit;

namespace PoeOverlay.Shell.Tests.Overlay;

/// <summary>
/// The one part of the parent-follows-child rule that a test can reach without a desktop.
/// </summary>
/// <remarks>
/// Auto height is now "the child resizes itself, the parent is told to match"
/// (<c>00-shell-measurements.md</c> §11.6). The rest of that rule is a <c>SetWindowPos</c> whose
/// effect is only observable on screen; what is testable is the unpacking, and it is the part with
/// a real trap in it — <c>IntPtr.ToInt64</c> sign-extends, so a height above 32,767 shifted out of
/// a signed value comes back with the sign bits of the width smeared into it.
/// </remarks>
public sealed class OverlayHostSizeDecodeTests
{
    [Fact]
    public void ATypicalOverlaySize_UnpacksToWidthThenHeight()
    {
        var (width, height) = OverlayHost.DecodeSize(new IntPtr((234 << 16) | 420));

        Assert.Equal(420, width);
        Assert.Equal(234, height);
    }

    [Fact]
    public void AZeroSizeIsNotConfusedWithAFailure()
    {
        var (width, height) = OverlayHost.DecodeSize(IntPtr.Zero);

        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Fact]
    public void AHighBitHeight_DoesNotSignExtendIntoTheWidth()
    {
        // A 40,000-pixel-tall window is absurd, but the same bit pattern arrives from any height
        // over 32,767 — and on a 64-bit IntPtr an arithmetic shift would report -25,536 here.
        var (width, height) = OverlayHost.DecodeSize(new IntPtr(unchecked((int)((40000u << 16) | 420u))));

        Assert.Equal(420, width);
        Assert.Equal(40000, height);
    }
}
