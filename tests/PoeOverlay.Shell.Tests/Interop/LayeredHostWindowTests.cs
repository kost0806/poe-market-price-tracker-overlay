using System.Windows;
using System.Windows.Interop;
using PoeOverlay.Composition;
using PoeOverlay.Interop;
using Xunit;

namespace PoeOverlay.Shell.Tests.Interop;

/// <summary>
/// The premise the whole overlay structure rests on, exercised against the live window manager.
/// </summary>
/// <remarks>
/// <para>
/// The application shipped an opaque overlay because <c>WS_EX_LAYERED</c> was written to a WPF
/// <c>Window</c>, the call reported success with <c>GetLastError == 0</c>, and nobody read the
/// style back — <c>SetLayeredWindowAttributes</c> then failed with 87
/// (<c>00-shell-measurements.md</c> §11.1). These tests are the read-back.
/// </para>
/// <para>
/// They create real windows but never show or pump them, and they need no message loop.
/// </para>
/// </remarks>
public sealed class LayeredHostWindowTests
{
    private const uint ColorKey = ShellConstants.ColorKeyRef;

    [Fact]
    public void TheRawParent_IsActuallyLayered_AndAcceptsTheKeyAndAlpha()
        => Sta(() =>
        {
            using var handle = Create(nameof(TheRawParent_IsActuallyLayered_AndAcceptsTheKeyAndAlpha));
            var gate = new ExtendedStyleGate(handle.Hwnd);

            // Read back, never assume. This is the assertion whose absence shipped the defect.
            Assert.True(
                (gate.Read() & ExtendedStyleBits.Layered) != 0,
                $"The parent read back as 0x{(uint)gate.Read():X8}, without WS_EX_LAYERED.");

            Assert.True(gate.SetLayered(ColorKey, 128, LwaFlags.ColorKey | LwaFlags.Alpha));
        });

    [Fact]
    public void TheRawParent_CarriesTheMeasuredAdoptedExtendedStyle()
        => Sta(() =>
        {
            using var handle = Create(nameof(TheRawParent_CarriesTheMeasuredAdoptedExtendedStyle));
            var style = new ExtendedStyleGate(handle.Hwnd).Read();

            // 00-shell-measurements.md §11.5, plus WS_EX_TRANSPARENT, which is on whenever
            // click-through is (the overlay starts click-through).
            const ExtendedStyleBits Expected = ExtendedStyleBits.Layered
                | ExtendedStyleBits.ToolWindow
                | ExtendedStyleBits.Topmost
                | ExtendedStyleBits.NoActivate
                | ExtendedStyleBits.Transparent;

            Assert.Equal(Expected, style & Expected);
        });

    [Fact]
    public void ClickThroughTogglesOffAndOn_WithoutDisturbingTheOtherBits()
        => Sta(() =>
        {
            using var handle = Create(nameof(ClickThroughTogglesOffAndOn_WithoutDisturbingTheOtherBits));
            var gate = new ExtendedStyleGate(handle.Hwnd);

            gate.ApplyAndNot(ExtendedStyleBits.Transparent);
            var duringMoveMode = gate.Read();
            Assert.Equal(ExtendedStyleBits.None, duringMoveMode & ExtendedStyleBits.Transparent);

            // NOACTIVATE in particular must survive: move mode drops click-through and nothing else
            // (S3 4.6 D-SH9).
            Assert.NotEqual(ExtendedStyleBits.None, duringMoveMode & ExtendedStyleBits.NoActivate);
            Assert.NotEqual(ExtendedStyleBits.None, duringMoveMode & ExtendedStyleBits.Layered);

            gate.ApplyOr(ExtendedStyleBits.Transparent);
            Assert.NotEqual(ExtendedStyleBits.None, gate.Read() & ExtendedStyleBits.Transparent);
        });

    [Fact]
    public void AWpfWindow_StillCannotBeLayered_WhichIsWhyTheRawParentExists()
        => Sta(() =>
        {
            // The negative control, and deliberately a canary: if this ever fails, the platform has
            // changed and §11.1 must be re-measured before anything is simplified on the strength
            // of it. It is not a statement that the behaviour is desirable.
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = false,
                ShowActivated = false,
                ShowInTaskbar = false,
            };

            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                var gate = new ExtendedStyleGate(hwnd);

                gate.ApplyOr(ExtendedStyleBits.Layered);

                Assert.Equal(ExtendedStyleBits.None, gate.Read() & ExtendedStyleBits.Layered);
                Assert.False(gate.SetLayered(ColorKey, 128, LwaFlags.ColorKey | LwaFlags.Alpha));
            }
            finally
            {
                window.Close();
            }
        });

    private static LayeredHostWindowHandle Create(string discriminator)
        => new LayeredHostWindowFactory().Create(
            $"{ShellConstants.OverlayWindowClassName}.{discriminator}",
            ShellConstants.OverlayWindowTitle,
            ColorKey,
            new NativeMethods.NativeRect { Left = 0, Top = 0, Right = 420, Bottom = 200 });

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
