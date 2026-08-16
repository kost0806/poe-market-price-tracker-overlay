using System.Globalization;
using PoeOverlay.Composition;
using Xunit;

namespace PoeOverlay.Shell.Tests.Composition;

/// <summary>
/// The three native dialog strings (S4 18.2 D-DL20).
/// </summary>
public sealed class NativeDialogTextTests
{
    [Fact]
    public void InstanceUnreachable_TakesTheLogFolder()
    {
        var text = string.Format(CultureInfo.InvariantCulture, NativeDialogText.InstanceUnreachable, @"C:\logs");
        Assert.Contains(@"C:\logs", text, StringComparison.Ordinal);
    }

    [Fact]
    public void InstanceUnreachable_DoesNotClaimTheOtherInstanceIsDead()
    {
        // A receiver busy inside the handler produces the same timeout as a dead one and then
        // raises its window a few seconds later, so the wording has to survive being wrong
        // (S3 3.2 M6).
        Assert.DoesNotContain("unreachable", NativeDialogText.InstanceUnreachable, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("should appear shortly", NativeDialogText.InstanceUnreachable, StringComparison.Ordinal);
    }

    [Fact]
    public void BootFailed_TakesTheDetailAndTheLogFolder()
    {
        var text = string.Format(CultureInfo.InvariantCulture, NativeDialogText.BootFailed, "boom", @"C:\logs");
        Assert.Contains("boom", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\logs", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindowUnavailable_TakesTheLogFolder()
    {
        var text = string.Format(CultureInfo.InvariantCulture, NativeDialogText.SettingsWindowUnavailable, @"C:\logs");
        Assert.Contains(@"C:\logs", text, StringComparison.Ordinal);
    }
}
