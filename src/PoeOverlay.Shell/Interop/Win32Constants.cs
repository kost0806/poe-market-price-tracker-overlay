namespace PoeOverlay.Interop;

/// <summary>
/// The Win32 numeric constants this application uses (S4 12.3 / S4 15.9).
/// </summary>
/// <remarks>
/// The four window bits are not transcribed from a header but read back off a live overlay: the
/// measurement recorded <c>GWL_EXSTYLE == 0x08080028</c> for the adopted configuration, which is
/// exactly <c>LAYERED | TRANSPARENT | NOACTIVATE | TOPMOST</c>
/// (<c>00-shell-measurements.md</c> §9). A regression test re-derives that sum.
/// </remarks>
internal static class Win32Constants
{
    /// <summary>Index of the extended style word in <c>GetWindowLong</c>.</summary>
    internal const int GwlExStyle = -20;

    /// <summary><c>WS_EX_LAYERED</c>.</summary>
    internal const uint WsExLayered = 0x00080000;

    /// <summary><c>WS_EX_TRANSPARENT</c> — hit testing falls through to whatever is behind.</summary>
    internal const uint WsExTransparent = 0x00000020;

    /// <summary><c>WS_EX_NOACTIVATE</c> — neutralises even an explicit <c>Activate()</c> (measured §3).</summary>
    internal const uint WsExNoActivate = 0x08000000;

    /// <summary><c>WS_EX_TOPMOST</c>. Written once, at creation, on the raw parent (S3 4.0).</summary>
    internal const uint WsExTopmost = 0x00000008;

    /// <summary><c>WS_EX_TOOLWINDOW</c> — keeps the overlay out of the taskbar and Alt+Tab.</summary>
    internal const uint WsExToolWindow = 0x00000080;

    /// <summary><c>WS_POPUP</c> — the parent has no caption, border or menu.</summary>
    internal const uint WsPopup = 0x80000000;

    /// <summary><c>WS_VISIBLE</c>.</summary>
    internal const uint WsVisible = 0x10000000;

    /// <summary>
    /// <c>WS_CLIPCHILDREN</c> — required, not optional.
    /// </summary>
    /// <remarks>
    /// Without it the parent's own background erase paints over the hosted child
    /// (<c>00-shell-measurements.md</c> §11.5).
    /// </remarks>
    internal const uint WsClipChildren = 0x02000000;

    /// <summary><c>WS_CHILD</c> — the hosted <c>HwndSource</c>.</summary>
    internal const uint WsChild = 0x40000000;

    /// <summary><c>WS_CLIPSIBLINGS</c> — the child's measured style (§11.5).</summary>
    internal const uint WsClipSiblings = 0x04000000;

    /// <summary><c>SWP_NOSIZE</c>.</summary>
    internal const uint SwpNoSize = 0x0001;

    /// <summary><c>SWP_NOMOVE</c>.</summary>
    internal const uint SwpNoMove = 0x0002;

    /// <summary><c>SWP_NOZORDER</c>.</summary>
    internal const uint SwpNoZOrder = 0x0004;

    /// <summary><c>SWP_NOACTIVATE</c>.</summary>
    internal const uint SwpNoActivate = 0x0010;

    /// <summary><c>SWP_NOOWNERZORDER</c>.</summary>
    internal const uint SwpNoOwnerZOrder = 0x0200;

    /// <summary><c>SW_SHOWNOACTIVATE</c> — shows without stealing the foreground (§3).</summary>
    internal const int SwShowNoActivate = 4;

    /// <summary><c>WM_SIZE</c>, watched on the child so the parent can follow it (§11.6).</summary>
    internal const int WmSize = 0x0005;

    /// <summary><c>IDC_ARROW</c>, as the <c>MAKEINTRESOURCE</c> value <c>LoadCursor</c> expects.</summary>
    internal static IntPtr IdcArrow => new(32512);

    /// <summary><c>LWA_COLORKEY</c>.</summary>
    internal const uint LwaColorKey = 0x00000001;

    /// <summary><c>LWA_ALPHA</c>.</summary>
    internal const uint LwaAlpha = 0x00000002;

    /// <summary><c>SMTO_ABORTIFHUNG</c> (S3 3.2).</summary>
    internal const uint SmtoAbortIfHung = 0x00000002;

    /// <summary><c>ASFW_ANY</c> — grants foreground rights to any process (see <c>NativeMethods</c>).</summary>
    internal const int AsfwAny = -1;

    /// <summary><c>CW_USEDEFAULT</c>.</summary>
    internal const int CwUseDefault = unchecked((int)0x80000000);

    /// <summary>Parent handle that makes a window message-only (S3 3.2 D-SH4).</summary>
    internal static IntPtr HwndMessage => new(-3);

    /// <summary><c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> — the settings window's caption (S3 5.4).</summary>
    /// <remarks>
    /// The documented value since Windows 10 20H1. Builds 18985 and earlier used 19 for the same
    /// attribute; this application does not chase that, it lets the older caption stay light.
    /// </remarks>
    internal const int DwmwaUseImmersiveDarkMode = 20;
}
