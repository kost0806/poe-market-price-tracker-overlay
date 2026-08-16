using System.Windows;
using PoeOverlay.Core.Presentation.ViewModels;

namespace PoeOverlay.Settings;

/// <summary>
/// The one window the user operates the application through (FR-08-2 / S3 5 / S4 12.6).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Window.Owner"/> is set to the overlay and that is not cosmetic. Ownership propagates
/// <c>WS_EX_TOPMOST</c> — measured exstyle <c>0x40108</c> for an owned window against
/// <c>0x40100</c> for an unowned one, even though the owned window's <c>Topmost</c> property still
/// reads false. Without it the z-order is <c>owned &gt; overlay &gt; unowned</c> and this window
/// opens <em>behind</em> the always-topmost overlay (<c>00-shell-measurements.md</c> §2).
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
    /// <param name="attribution">The poe.ninja attribution line (NFR-05).</param>
    /// <param name="owner">The overlay. Required — see the type remarks.</param>
    public SettingsWindow(SettingsViewModel viewModel, SettingsEditor editor, string attribution, Window owner)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(owner);

        Editor = editor;
        InitializeComponent();

        DataContext = viewModel;
        Owner = owner;
        Attribution.Text = attribution;
    }

    /// <summary>The scalar settings adapter, bound to by name from the XAML.</summary>
    public SettingsEditor Editor { get; }
}
