using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PoeOverlay.Interop;

/// <summary>
/// Creates the <c>HWND_MESSAGE</c> window the single-instance signal is received on (S4 12.5 B5).
/// </summary>
/// <remarks>
/// A message-only window can exist from step 8 of the boot sequence, long before the overlay HWND
/// does; hanging signal reception off the overlay instead would leave the 8→9 window deaf
/// (S3 3.2 D-SH4).
/// </remarks>
internal sealed class MessageOnlyWindowFactory
{
    /// <summary>Registers a class and creates one message-only window on the calling thread.</summary>
    /// <param name="className">Class name to register; must be unique within the process.</param>
    /// <param name="windowTitle">Window text. Not used for discovery — the sender matches on the class.</param>
    /// <param name="wndProc">Returns the value to reply with, or null to fall through to <c>DefWindowProc</c>.</param>
    /// <returns>A handle that destroys the window and unregisters the class on disposal.</returns>
    internal MessageOnlyWindowHandle Create(string className, string windowTitle, Func<uint, IntPtr, IntPtr, IntPtr?> wndProc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentNullException.ThrowIfNull(wndProc);

        var instance = NativeMethods.GetModuleHandleW(null);

        // Rooted in the handle: letting the GC collect this delegate while the class is registered
        // turns the next message into an access violation, not an exception.
        NativeMethods.WindowProc procedure = (hwnd, msg, wParam, lParam) =>
            wndProc(msg, wParam, lParam) ?? NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);

        var windowClass = new NativeMethods.WndClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(procedure),
            Instance = instance,
            ClassName = className,
        };

        var atom = NativeMethods.RegisterClassExW(ref windowClass);
        if (atom == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"RegisterClassEx failed for '{className}'.");
        }

        var hwnd = NativeMethods.CreateWindowExW(
            0,
            className,
            windowTitle,
            0,
            0,
            0,
            0,
            0,
            Win32Constants.HwndMessage,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _ = NativeMethods.UnregisterClassW(className, instance);
            throw new Win32Exception(error, $"CreateWindowEx failed for '{className}'.");
        }

        return new MessageOnlyWindowHandle(hwnd, className, instance, procedure);
    }
}

/// <summary>Owns one message-only window and the class registration behind it.</summary>
internal sealed class MessageOnlyWindowHandle : IDisposable
{
    private readonly string _className;
    private readonly IntPtr _instance;

    // Held only to keep the delegate alive for as long as the window can receive messages.
    private readonly NativeMethods.WindowProc _procedure;

    private int _disposed;

    internal MessageOnlyWindowHandle(IntPtr hwnd, string className, IntPtr instance, NativeMethods.WindowProc procedure)
    {
        Hwnd = hwnd;
        _className = className;
        _instance = instance;
        _procedure = procedure;
    }

    /// <summary>The window handle. <see cref="IntPtr.Zero"/> once disposed.</summary>
    internal IntPtr Hwnd { get; private set; }

    /// <summary>Destroys the window and unregisters the class. Idempotent.</summary>
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
        GC.KeepAlive(_procedure);
    }
}
