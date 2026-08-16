using System.Runtime.InteropServices;

namespace PoeOverlay.Interop;

/// <summary>
/// Every P/Invoke in the application (S3 1.1 — Win32 does not leak past <c>Interop/</c>).
/// </summary>
/// <remarks>
/// <c>AttachThreadInput</c> is deliberately absent and must stay absent: the measurement showed it
/// defeats the foreground lock outright, succeeding with zero user input
/// (<c>00-shell-measurements.md</c> §1.4). It is a bypass, not a workaround.
/// </remarks>
internal static class NativeMethods
{
    /// <summary>Window procedure signature. Instances must be rooted for the window's lifetime.</summary>
    internal delegate IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Reads the extended style word, 32- and 64-bit safe.
    /// </summary>
    /// <remarks>
    /// <c>GetWindowLongPtrW</c> does not exist in 32-bit <c>user32</c>, so the pointer-width branch
    /// is not optional.
    /// </remarks>
    internal static uint GetExtendedStyle(IntPtr hwnd)
        => IntPtr.Size == 8
            ? unchecked((uint)(long)GetWindowLongPtrW(hwnd, Win32Constants.GwlExStyle))
            : unchecked((uint)GetWindowLongW(hwnd, Win32Constants.GwlExStyle));

    /// <summary>Writes the extended style word, 32- and 64-bit safe.</summary>
    internal static void SetExtendedStyle(IntPtr hwnd, uint value)
    {
        if (IntPtr.Size == 8)
        {
            _ = SetWindowLongPtrW(hwnd, Win32Constants.GwlExStyle, unchecked((IntPtr)(long)value));
        }
        else
        {
            _ = SetWindowLongW(hwnd, Win32Constants.GwlExStyle, unchecked((int)value));
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    internal static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorName);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageTimeoutW(
        IntPtr hwnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMs,
        out IntPtr result);

    /// <summary>
    /// Grants foreground rights away from this process.
    /// </summary>
    /// <remarks>
    /// Only meaningful when handing the right to <em>another</em> process — applying it to oneself
    /// does nothing (measured §1.4). The second instance calls it with <c>ASFW_ANY</c> immediately
    /// before signalling, so the first instance may raise its settings window.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>The only trustworthy activation oracle: <c>IsActive</c> was true in every failure (measured §1.4).</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandleW(string? moduleName);

    /// <summary><c>RECT</c>, in physical pixels.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        /// <summary>Left edge.</summary>
        internal int Left;

        /// <summary>Top edge.</summary>
        internal int Top;

        /// <summary>Right edge, exclusive.</summary>
        internal int Right;

        /// <summary>Bottom edge, exclusive.</summary>
        internal int Bottom;
    }

    /// <summary><c>POINT</c>, in physical pixels.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        /// <summary>Horizontal coordinate.</summary>
        internal int X;

        /// <summary>Vertical coordinate.</summary>
        internal int Y;
    }

    /// <summary><c>WNDCLASSEXW</c>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClassEx
    {
        internal uint Size;
        internal uint Style;
        internal IntPtr WndProc;
        internal int ClsExtra;
        internal int WndExtra;
        internal IntPtr Instance;
        internal IntPtr Icon;
        internal IntPtr Cursor;
        internal IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string ClassName;
        internal IntPtr IconSm;
    }
}
