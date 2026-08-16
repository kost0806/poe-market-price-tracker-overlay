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

    /// <summary><c>WS_EX_TOPMOST</c>. Never written by hand; WPF's <c>Topmost</c> sets it.</summary>
    internal const uint WsExTopmost = 0x00000008;

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
}
