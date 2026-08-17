using System.Windows;
using System.Windows.Interop;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Interop;

namespace PoeOverlay.Settings;

/// <summary>
/// The one window the user operates the application through (FR-08-2 / S3 5 / S4 12.6).
/// </summary>
/// <remarks>
/// <para>
/// The overlay owns this window and that is not cosmetic. Ownership propagates
/// <c>WS_EX_TOPMOST</c> — measured exstyle <c>0x40108</c> for an owned window against
/// <c>0x40100</c> for an unowned one, even though the owned window's <c>Topmost</c> property still
/// reads false. Without it the z-order is <c>owned &gt; overlay &gt; unowned</c> and this window
/// opens <em>behind</em> the always-topmost overlay (<c>00-shell-measurements.md</c> §2).
/// </para>
/// <para>
/// The owner arrives as an HWND rather than a <c>Window</c>: the overlay is a raw Win32 parent now
/// (S3 4.0 D-SH20), and <c>WindowInteropHelper.Owner</c> sets <c>GWLP_HWNDPARENT</c>, which is the
/// thing the measurement above was measuring (§11.6). The one cost is
/// <c>WindowStartupLocation</c>: <c>CenterOwner</c> reads the managed <c>Owner</c> property, which
/// is now null, so the XAML asks for <c>CenterScreen</c> instead.
/// </para>
/// <para>
/// Activation needs no help. <c>Show(); Activate();</c> from a tray click took the foreground even
/// with a borderless fullscreen topmost window holding it, and all six activation variants
/// succeeded identically — so no retry loop, no temporary <c>Topmost</c>, and above all no
/// <c>AttachThreadInput</c>, which succeeded with zero user input and is therefore a lock bypass
/// (measured §1.1, §1.4).
/// </para>
/// </remarks>
public sealed partial class SettingsWindow : Window
{
    /// <summary>Builds the settings window.</summary>
    /// <param name="viewModel">The window-scoped view model; disposed when the window closes.</param>
    /// <param name="editor">The five scalar settings Presentation's surface does not carry.</param>
    /// <param name="owner">The overlay's HWND. Required — see the type remarks.</param>
    /// <remarks>
    /// The attribution line used to arrive here as a fourth argument and was written once into a
    /// named <c>TextBlock</c>. It is <c>viewModel.Strings.Attribution</c> now (S3 5.4.4): written
    /// once, it was the one string on this window a language change could not reach.
    /// </remarks>
    public SettingsWindow(SettingsViewModel viewModel, SettingsEditor editor, IntPtr owner)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(editor);

        if (owner == IntPtr.Zero)
        {
            throw new ArgumentException(
                "The settings window must be owned by the overlay, or it opens behind it.",
                nameof(owner));
        }

        Editor = editor;
        InitializeComponent();

        DataContext = viewModel;

        // Before the source is created, so the ownership is in place from the first CreateWindowEx
        // rather than being retro-fitted to a window that has already been placed in the z-order.
        new WindowInteropHelper(this).Owner = owner;
    }

    /// <summary>The scalar settings adapter, bound to by name from the XAML.</summary>
    public SettingsEditor Editor { get; }

    /// <summary>
    /// Asks DWM for a dark caption, once the HWND exists (S3 5.4).
    /// </summary>
    /// <remarks>
    /// The window's own chrome is the one surface XAML cannot reach. A refusal is left alone: the
    /// attribute is simply unsupported on older builds and the caption stays light, which is worse
    /// looking and nothing more.
    /// </remarks>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var enabled = 1;
        _ = NativeMethods.DwmSetWindowAttribute(
            new WindowInteropHelper(this).Handle,
            Win32Constants.DwmwaUseImmersiveDarkMode,
            ref enabled,
            sizeof(int));
    }
}
