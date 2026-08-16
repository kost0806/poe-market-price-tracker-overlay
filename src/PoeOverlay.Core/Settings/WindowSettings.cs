using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Settings;

/// <summary>
/// Overlay window geometry as persisted (S2 8.1 / S4 10.1).
/// </summary>
/// <remarks>
/// <see cref="HeightMode"/> exists because <c>SizeToContent="Height"</c> overwrites the Height
/// dependency property on every layout pass (D19), so "the height the user chose" and "the height
/// the window currently has" are different facts and only the first one may be stored.
/// <para>
/// Screen fitness is deliberately not validated here: work areas are a pixel and monitor concept
/// and belong to Shell (S2 8.2). Settings only checks that the numbers are finite and in range.
/// </para>
/// </remarks>
public sealed record WindowSettings(
    double X,
    double Y,
    double Width,
    double Height,
    HeightMode HeightMode,
    double Opacity)
{
    /// <summary>HLD 7 defaults, transcribed in S4 15.1.</summary>
    public static WindowSettings Default { get; } = new(100d, 100d, 420d, 500d, HeightMode.Auto, 0.87d);
}
