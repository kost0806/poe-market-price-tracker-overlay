using PoeOverlay.Composition;
using PoeOverlay.Interop;
using Xunit;

namespace PoeOverlay.Shell.Tests.Interop;

/// <summary>
/// Pins the numeric constants against the values that were actually measured.
/// </summary>
/// <remarks>
/// These are cheap tests for a reason: the four style bits are not transcribed from a header, they
/// are the decomposition of a value read back off a live overlay, and a typo in one of them
/// produces a window that looks almost right and behaves wrongly in ways no unit test can reach.
/// </remarks>
public sealed class Win32ConstantTests
{
    /// <summary>The measured extended style of the adopted overlay configuration.</summary>
    private const uint MeasuredOverlayExStyle = 0x08080028;

    [Fact]
    public void StyleBits_ComposeTheMeasuredOverlayExStyle()
    {
        var composed = (uint)(ExtendedStyleBits.Layered
            | ExtendedStyleBits.Transparent
            | ExtendedStyleBits.NoActivate
            | ExtendedStyleBits.Topmost);

        Assert.Equal(MeasuredOverlayExStyle, composed);
    }

    [Fact]
    public void StyleBits_AreDisjoint()
    {
        Assert.Equal(0u, Win32Constants.WsExLayered & Win32Constants.WsExTransparent);
        Assert.Equal(0u, Win32Constants.WsExLayered & Win32Constants.WsExNoActivate);
        Assert.Equal(0u, Win32Constants.WsExTransparent & Win32Constants.WsExNoActivate);
    }

    [Fact]
    public void HwndMessage_IsMinusThree()
        => Assert.Equal(new IntPtr(-3), Win32Constants.HwndMessage);

    [Fact]
    public void AckSentinel_IsTheMeasuredProbeValue()
        => Assert.Equal(12345, ShellConstants.AckSentinel);

    [Fact]
    public void ColorKey_IsSymmetricUnderTheColorRefByteSwap()
    {
        // 0x00BBGGRR: red and blue both 255, so the swap is a no-op. If the palette ever moves off
        // magenta this test is the reminder that the literal is a COLORREF, not an RGB value.
        var r = ShellConstants.ColorKeyRef & 0xFF;
        var b = (ShellConstants.ColorKeyRef >> 16) & 0xFF;
        Assert.Equal(r, b);
    }

    [Fact]
    public void SendRetryBudget_CoversThePumplessWindow()
    {
        // SendMessageTimeout does not queue, so the sender's total budget is the only cover for the
        // stretch between the receiver being created and app.Run() starting (S3 3.2 C2).
        var budget = ShellConstants.SendAttemptTimeout * ShellConstants.SendAttempts;
        Assert.True(budget >= TimeSpan.FromSeconds(6), $"Send budget was only {budget}.");
    }
}
