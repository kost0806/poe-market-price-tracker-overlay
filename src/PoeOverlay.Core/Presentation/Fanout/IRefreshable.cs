using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Presentation.Fanout;

/// <summary>
/// What <see cref="SnapshotFanout"/> calls on each attached view model (S3 8.0 D-PS9 / S4 11.2).
/// </summary>
/// <remarks>
/// <para>
/// <c>Refresh</c> is a pure read plus display-state calculation. It must not raise a condition,
/// report an error or update settings: all three put a command on the Store, which publishes a new
/// snapshot and schedules another pass (S3 8.4). <see cref="UiPassGuard"/> enforces that contract.
/// </para>
/// <para>
/// <paramref name="now"/> is an argument rather than a clock read so that one pass shares one
/// instant — rows computed against different clocks can disagree about whether the rate expired,
/// a defect that reproduces only as a screenshot (S3 9.2, D-PR7).
/// </para>
/// </remarks>
public interface IRefreshable
{
    /// <summary>Recomputes display state from <paramref name="snapshot"/> as of <paramref name="now"/>.</summary>
    void Refresh(MarketSnapshot snapshot, DateTimeOffset now);
}
