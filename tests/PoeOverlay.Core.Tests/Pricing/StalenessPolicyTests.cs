using PoeOverlay.Core.Pricing;
using Xunit;

namespace PoeOverlay.Core.Tests.Pricing;

/// <summary>
/// S2 4.5.3, and the one derived rule S2 11 says Core.Tests can reach — the three thresholds are
/// pure functions of the refresh interval.
/// </summary>
/// <remarks>
/// They live in one place because Polling's inheritance rule and Pricing's gate must agree: were
/// Polling to inherit a rate that Pricing judged expired, the store's rate would never reach the
/// screen.
/// </remarks>
public sealed class StalenessPolicyTests
{
    [Theory]
    [InlineData(5, 30)]     // the floor wins for the default interval
    [InlineData(10, 30)]    // 3 x 10 == the floor
    [InlineData(11, 33)]    // and past it, three intervals win
    [InlineData(60, 180)]
    public void RateMaxAge_IsTheLargerOfThirtyMinutesAndThreeIntervals(int interval, int expectedMinutes)
        => Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), StalenessPolicy.RateMaxAge(interval));

    [Theory]
    [InlineData(5, 10)]
    [InlineData(60, 120)]
    public void RowStaleAfter_IsTwoIntervals(int interval, int expectedMinutes)
        => Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), StalenessPolicy.RowStaleAfter(interval));

    [Theory]
    [InlineData(5, 11)]
    [InlineData(60, 121)]
    public void HeartbeatStaleAfter_IsTwoIntervalsPlusAMinute(int interval, int expectedMinutes)
        => Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), StalenessPolicy.HeartbeatStaleAfter(interval));

    [Fact]
    public void HeartbeatIsAlwaysJudgedLaterThanARow()
        => Assert.True(StalenessPolicy.HeartbeatStaleAfter(5) > StalenessPolicy.RowStaleAfter(5));
}
