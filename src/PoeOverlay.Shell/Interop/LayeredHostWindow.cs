using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PoeOverlay.Interop;

/// <summary>
/// Creates the raw Win32 parent the overlay's WPF content is hosted inside (S3 4.0, D-SH20).
/// </summary>
/// <remarks>
/// <para>
/// A WPF <see cref="System.Windows.Window"/> cannot be used here. With
/// <c>AllowsTransparency=false</c> the layered bit is silently filtered out of every write —
/// measured four ways, all returning success with <c>GetLastError == 0</c> while
/// <c>GWL_EXSTYLE</c> reads back without <c>WS_EX_LAYERED</c>, after which
/// <c>SetLayeredWindowAttributes</c> fails with <c>ERROR_INVALID_PARAMETER</c>
/// (<c>00-shell-measurements.md</c> §11.1). A raw popup in the same process accepts the bit
/// immediately, and the parent's colour key and alpha then apply to the pixels the hosted WPF
/// child draws, indistinguishably from parent-drawn pixels and with ClearType intact (§11.2, §11.3).
/// </para>
/// <para>
/// The class background brush is the colour key, so every pixel the child does not cover is keyed
/// out. <c>WS_CLIPCHILDREN</c> is what stops that erase from painting over the child; without it
/// the parent overdraws the content (§11.5).
/// </para>
/// </remarks>
internal sealed class LayeredHostWindowFactory
{
    /// <summary>Registers a class and creates one hidden layered popup on the calling thread.</summary>
    /// <param name="className">Class name to register; must be unique within the process.</param>
    /// <param name="windowTitle">Window text. Nothing discovers the overlay by it.</param>
    /// <param name="colorKeyRef">The colour key as a <c>COLORREF</c>; also the erase brush.</param>
    /// <param name="bounds">Initial physical-pixel bounds.</param>
    /// <returns>A handle that destroys the window, the brush and the class registration.</returns>
    internal LayeredHostWindowHandle Create(
        string className,
        string windowTitle,
        uint colorKeyRef,
        NativeMethods.NativeRect bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);

        var instance = NativeMethods.GetModuleHandleW(null);

        // Rooted in the handle: letting the GC collect this delegate while the class is registered
        // turns the next message into an access violation, not an exception.
        NativeMethods.WindowProc procedure = NativeMethods.DefWindowProcW;

        var brush = NativeMethods.CreateSolidBrush(colorKeyRef);
        if (brush == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateSolidBrush failed for the colour key.");
        }

        var windowClass = new NativeMethods.WndClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(procedure),
            Instance = instance,
            Cursor = NativeMethods.LoadCursorW(IntPtr.Zero, Win32Constants.IdcArrow),
            Background = brush,
            ClassName = className,
        };

        var atom = NativeMethods.RegisterClassExW(ref windowClass);
        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            _ = NativeMethods.DeleteObject(brush);
            throw new Win32Exception(error, $"RegisterClassEx failed for '{className}'.");
        }

        const uint ExStyle = Win32Constants.WsExLayered
            | Win32Constants.WsExToolWindow
            | Win32Constants.WsExTopmost
            | Win32Constants.WsExNoActivate
            | Win32Constants.WsExTransparent;

        var hwnd = NativeMethods.CreateWindowExW(
            ExStyle,
            className,
            windowTitle,
            Win32Constants.WsPopup | Win32Constants.WsClipChildren,
            bounds.Left,
            bounds.Top,
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _ = NativeMethods.UnregisterClassW(className, instance);
            _ = NativeMethods.DeleteObject(brush);
            throw new Win32Exception(error, $"CreateWindowEx failed for '{className}'.");
        }

        return new LayeredHostWindowHandle(hwnd, className, instance, brush, procedure);
    }
}

/// <summary>Owns one layered parent window, its erase brush and the class registration behind it.</summary>
internal sealed class LayeredHostWindowHandle : IDisposable
{
    private readonly string _className;
    private readonly IntPtr _instance;
    private readonly IntPtr _brush;

    // Held only to keep the delegate alive for as long as the window can receive messages.
    private readonly NativeMethods.WindowProc _procedure;

    private int _disposed;

    /// <summary>Wraps a live window.</summary>
    /// <param name="hwnd">The window handle.</param>
    /// <param name="className">The registered class name.</param>
    /// <param name="instance">The module the class belongs to.</param>
    /// <param name="brush">The colour-key erase brush.</param>
    /// <param name="procedure">The rooted window procedure.</param>
    internal LayeredHostWindowHandle(
        IntPtr hwnd,
        string className,
        IntPtr instance,
        IntPtr brush,
        NativeMethods.WindowProc procedure)
    {
        Hwnd = hwnd;
        _className = className;
        _instance = instance;
        _brush = brush;
        _procedure = procedure;
    }

    /// <summary>The window handle. <see cref="IntPtr.Zero"/> once disposed.</summary>
    internal IntPtr Hwnd { get; private set; }

    /// <summary>Shows the window without taking the foreground.</summary>
    internal void ShowNoActivate()
    {
        if (Hwnd != IntPtr.Zero)
        {
            _ = NativeMethods.ShowWindow(Hwnd, Win32Constants.SwShowNoActivate);
        }
    }

    /// <summary>Destroys the window, the brush and the class. Idempotent.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (Hwnd != IntPtr.Zero)
        {
            _ = NativeMethods.DestroyWindow(Hwnd);
            Hwnd = IntPtr.Zero;
        }

        _ = NativeMethods.UnregisterClassW(_className, _instance);
        _ = NativeMethods.DeleteObject(_brush);
        GC.KeepAlive(_procedure);
    }
}
