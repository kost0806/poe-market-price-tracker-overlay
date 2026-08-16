using PoeOverlay.Core.Settings;
using PoeOverlay.Startup;
using Xunit;

namespace PoeOverlay.Shell.Tests.Startup;

/// <summary>The first-run test is the flag's absence, not the file's (FR-08-6 / S3 6.5 P2).</summary>
public sealed class FirstRunGateTests
{
    [Fact]
    public void DefaultSettingsAskForTheGuidance()
        => Assert.True(FirstRunGate.ShouldAutoShowSettings(AppSettings.Default));

    [Fact]
    public void AcknowledgedSettingsDoNot()
        => Assert.False(FirstRunGate.ShouldAutoShowSettings(AppSettings.Default with { FirstRunAcknowledged = true }));

    [Fact]
    public void AFileThatExistsButLacksTheFlagStillAsksForIt()
    {
        // A user upgrading from a schema without the flag meets the same measured problem a new
        // user does — Windows 11 files a fresh tray icon into the overflow flyout — so there is no
        // reason to except them.
        var upgraded = AppSettings.Default with { League = "Settlers", FirstRunAcknowledged = false };
        Assert.True(FirstRunGate.ShouldAutoShowSettings(upgraded));
    }
}
