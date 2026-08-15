using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Pricing;
using PoeOverlay.Core.Tests.Localization;
using Xunit;

namespace PoeOverlay.Core.Tests.Pricing;

/// <summary>
/// S2 11.5 — relative time truncates rather than rounds, and clamps a clock that has run backwards.
/// </summary>
/// <remarks>
/// Under-stating the age is safe only because the staleness <em>verdict</em> is a raw
/// <see cref="TimeSpan"/> comparison in <see cref="StalenessPolicy"/>; the two must not be confused.
/// </remarks>
public sealed class RelativeTimeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 6, 0, 0, TimeSpan.Zero);

    private readonly LocalizationHarness _harness = LocalizationHarness.Create();
    private readonly LocalizationCatalog _templates;

    public RelativeTimeTests() => _templates = _harness.Start();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void UnderTenSeconds_IsJustNow()
        => Assert.Equal("just now", Relative(TimeSpan.FromSeconds(9)));

    [Fact]
    public void JustOverAMinute_IsWholeMinutes()
        => Assert.Equal("1m ago", Relative(TimeSpan.FromSeconds(61)));

    [Fact]
    public void JustUnderAnHour_IsStillMinutes()
        => Assert.Equal("59m ago", Relative(new TimeSpan(0, 59, 59)));

    [Fact]
    public void JustOverADay_IsWholeDays()
        => Assert.Equal("1d ago", Relative(TimeSpan.FromHours(25)));

    [Fact]
    public void ClockRunBackwards_IsClampedToJustNow()
        => Assert.Equal("just now", Relative(TimeSpan.FromSeconds(-5)));

    [Theory]
    [InlineData(10, "10s ago")]
    [InlineData(59, "59s ago")]
    public void BetweenTenAndSixtySeconds_IsWholeSeconds(int seconds, string expected)
        => Assert.Equal(expected, Relative(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void TruncationNeverRoundsUp()
        => Assert.Equal("2m ago", Relative(new TimeSpan(0, 2, 59)));

    [Fact]
    public void AnHourExactly_MovesToHours()
        => Assert.Equal("1h ago", Relative(TimeSpan.FromHours(1)));

    [Fact]
    public void LargeAges_AreNotGrouped()
        => Assert.Equal("1000d ago", Relative(TimeSpan.FromDays(1000)));

    private string Relative(TimeSpan age) => PricingEngine.Relative(Now - age, Now, _templates);
}
