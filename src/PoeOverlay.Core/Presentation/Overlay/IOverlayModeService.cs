namespace PoeOverlay.Core.Presentation.Overlay;

/// <summary>Why move mode ended (S4 11.7).</summary>
public enum MoveModeExitReason
{
    /// <summary>The settings window toggle was switched off.</summary>
    SettingsToggleOff,

    /// <summary>The tray context menu item was used.</summary>
    TrayMenu,

    /// <summary>The inactivity watchdog expired (S4 15.7 — five minutes).</summary>
    WatchdogTimeout,
}

/// <summary>
/// Move mode, declared here and implemented in the Shell (HLD D4-b, S3 7.3 / S4 11.7).
/// </summary>
/// <remarks>
/// The ordering rule — release capture, settle geometry, restore the style bits — is sealed inside
/// the implementation. A view model asks for the transition and never learns the sequence, which is
/// what keeps the extended-style read-modify-write in one place. UI-thread affine.
/// </remarks>
public interface IOverlayModeService
{
    /// <summary>True while move mode is active.</summary>
    bool IsActive { get; }

    /// <summary>Raised after <see cref="IsActive"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Enters move mode.</summary>
    void EnterMoveMode();

    /// <summary>Leaves move mode, recording why.</summary>
    void ExitMoveMode(MoveModeExitReason reason);
}
