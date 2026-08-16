namespace PoeOverlay.Interop;

/// <summary>The extended-style bits this application ever sets (S4 12.3).</summary>
[Flags]
public enum ExtendedStyleBits : uint
{
    /// <summary>No bits.</summary>
    None = 0,

    /// <summary><c>WS_EX_LAYERED</c>.</summary>
    Layered = Win32Constants.WsExLayered,

    /// <summary><c>WS_EX_TRANSPARENT</c>.</summary>
    Transparent = Win32Constants.WsExTransparent,

    /// <summary><c>WS_EX_NOACTIVATE</c>.</summary>
    NoActivate = Win32Constants.WsExNoActivate,

    /// <summary><c>WS_EX_TOPMOST</c> — read only; the raw parent is created with it.</summary>
    Topmost = Win32Constants.WsExTopmost,

    /// <summary><c>WS_EX_TOOLWINDOW</c> — read only; the raw parent is created with it.</summary>
    ToolWindow = Win32Constants.WsExToolWindow,
}

/// <summary>Which fields of <c>SetLayeredWindowAttributes</c> are meaningful.</summary>
[Flags]
public enum LwaFlags
{
    /// <summary>The colour key is meaningful.</summary>
    ColorKey = 1,

    /// <summary>The uniform alpha is meaningful.</summary>
    Alpha = 2,
}

/// <summary>
/// The only route to <c>SetWindowLong</c> and <c>SetLayeredWindowAttributes</c> (S3 4.1 / S4 12.3).
/// </summary>
/// <remarks>
/// There is no whole-word setter, by construction. HLD D4-d's read-modify-write discipline exists
/// because a wholesale assignment silently drops bits somebody else set; re-implementing the
/// read-modify-write at each call site is how that accident comes back, so the type exposes only
/// <see cref="ApplyOr"/> and <see cref="ApplyAndNot"/>.
/// <para>
/// Re-assertion after resize or a monitor move is <em>not</em> wanted: the measurement stepped a
/// live overlay through sixteen resize and cross-monitor stages and <c>GWL_EXSTYLE</c> stayed at
/// <c>0x08080028</c> throughout, losing z-order zero times (<c>00-shell-measurements.md</c> §9).
/// Defensive re-application would be code with no defect behind it.
/// </para>
/// </remarks>
public sealed class ExtendedStyleGate
{
    private readonly IntPtr _hwnd;

    /// <summary>Creates a gate over a live window handle.</summary>
    /// <param name="hwnd">A window handle; only valid from <c>SourceInitialized</c> onwards.</param>
    public ExtendedStyleGate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("The window handle must exist before the gate is built.", nameof(hwnd));
        }

        _hwnd = hwnd;
    }

    /// <summary>Defers construction until <c>SourceInitialized</c>, when the handle first exists.</summary>
    /// <param name="hwnd">The window handle.</param>
    /// <returns>A gate over <paramref name="hwnd"/>.</returns>
    public delegate ExtendedStyleGate Factory(IntPtr hwnd);

    /// <summary>Reads the current extended style word.</summary>
    /// <returns>The bits currently set.</returns>
    public ExtendedStyleBits Read() => (ExtendedStyleBits)NativeMethods.GetExtendedStyle(_hwnd);

    /// <summary>Turns <paramref name="mask"/> on, leaving every other bit alone.</summary>
    /// <param name="mask">Bits to set.</param>
    public void ApplyOr(ExtendedStyleBits mask)
    {
        var current = NativeMethods.GetExtendedStyle(_hwnd);
        NativeMethods.SetExtendedStyle(_hwnd, current | (uint)mask);
    }

    /// <summary>Turns <paramref name="mask"/> off, leaving every other bit alone.</summary>
    /// <param name="mask">Bits to clear.</param>
    public void ApplyAndNot(ExtendedStyleBits mask)
    {
        var current = NativeMethods.GetExtendedStyle(_hwnd);
        NativeMethods.SetExtendedStyle(_hwnd, current & ~(uint)mask);
    }

    /// <summary>Applies the colour key and the window-wide alpha.</summary>
    /// <param name="colorKeyRgb">The key colour as <c>0x00BBGGRR</c> (a COLORREF, not a hex RGB literal).</param>
    /// <param name="alpha">Uniform window alpha, 0–255.</param>
    /// <param name="flags">Which of the two are meaningful.</param>
    /// <returns>
    /// True when the call succeeded. The result is returned rather than discarded because the one
    /// configuration this application ever shipped that could not be layered failed exactly here,
    /// with <c>ERROR_INVALID_PARAMETER</c>, while every preceding style write reported success
    /// (<c>00-shell-measurements.md</c> §11.1).
    /// </returns>
    public bool SetLayered(uint colorKeyRgb, byte alpha, LwaFlags flags)
    {
        uint native = 0;
        if ((flags & LwaFlags.ColorKey) != 0)
        {
            native |= Win32Constants.LwaColorKey;
        }

        if ((flags & LwaFlags.Alpha) != 0)
        {
            native |= Win32Constants.LwaAlpha;
        }

        return NativeMethods.SetLayeredWindowAttributes(_hwnd, colorKeyRgb, alpha, native);
    }
}
