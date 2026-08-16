namespace PoeOverlay.Core.Presentation.Overlay;

/// <summary>
/// The settings window's only route to the overlay's geometry (S3 4.3.1 D-PS7, 7.5 / S4 11.7).
/// </summary>
/// <remarks>
/// Both commands compute pixel values and must therefore be implemented in the Shell; routing them
/// through this port is what keeps D19's single-writer invariant intact — the settings window never
/// writes window geometry itself. UI-thread affine.
/// </remarks>
public interface IOverlayGeometryService
{
    /// <summary>Returns the overlay to the default position and size (HLD D22).</summary>
    void ResetPlacement();

    /// <summary>Switches the height policy back to content-driven (S3 4.4 D-SH7).</summary>
    void RevertHeightToAuto();
}
