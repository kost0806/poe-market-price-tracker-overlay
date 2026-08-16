using System.Windows;
using Size = System.Windows.Size;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Overlay;

/// <summary>
/// The minimum-visible-area rule (S3 4.5 D-SH8 / S4 12.3).
/// </summary>
/// <remarks>
/// HLD 7 originally demanded the window be fully contained in one work area, which rejects
/// legitimate placements — a window slightly taller than the work area, or one deliberately spanning
/// two monitors. The footer is already defined as the region that must never be clipped (D19), so
/// it is the honest definition of "enough of this window is visible to be useful".
/// </remarks>
internal static class OverlayGeometryValidator
{
    /// <summary>True when some work area shows at least a whole footer's worth of the window.</summary>
    /// <param name="windowBounds">Where the window would sit, in DIPs.</param>
    /// <param name="workAreas">Every screen's work area, in DIPs.</param>
    /// <param name="footerSize">The footer's width and height.</param>
    /// <returns>True when the placement is acceptable.</returns>
    internal static bool HasMinimumVisibleArea(Rect windowBounds, IReadOnlyList<Rect> workAreas, Size footerSize)
        => BestWorkArea(windowBounds, workAreas, footerSize) is not null;

    /// <summary>
    /// The work area the window is judged against — the one showing most of it.
    /// </summary>
    /// <param name="windowBounds">Where the window would sit, in DIPs.</param>
    /// <param name="workAreas">Every screen's work area, in DIPs.</param>
    /// <param name="footerSize">The footer's width and height.</param>
    /// <returns>The chosen work area, or null when no area shows a whole footer.</returns>
    /// <remarks>
    /// The clipping formula of S3 4.4.1 needs the same answer this validation produced; computing it
    /// twice from two rules is how the two drift apart.
    /// </remarks>
    internal static Rect? BestWorkArea(Rect windowBounds, IReadOnlyList<Rect> workAreas, Size footerSize)
    {
        ArgumentNullException.ThrowIfNull(workAreas);

        Rect? best = null;
        var bestArea = 0d;

        foreach (var area in workAreas)
        {
            var overlap = Rect.Intersect(area, windowBounds);
            if (overlap.IsEmpty)
            {
                continue;
            }

            // The footer is a full-width strip, so partial width is not enough; a sliver of the
            // window's left edge shows no attribution and no update time.
            var needsWidth = Math.Min(footerSize.Width, windowBounds.Width);
            if (overlap.Width + Tolerance < needsWidth || overlap.Height + Tolerance < footerSize.Height)
            {
                continue;
            }

            var size = overlap.Width * overlap.Height;
            if (size > bestArea)
            {
                bestArea = size;
                best = area;
            }
        }

        return best;
    }

    /// <summary>The default position a rejected placement falls back to.</summary>
    /// <returns>The default X and Y from HLD 7.</returns>
    internal static (double X, double Y) ClampToDefault()
        => (WindowSettings.Default.X, WindowSettings.Default.Y);

    private const double Tolerance = 0.5d;
}
