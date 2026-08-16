using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Settings;
using Xunit;

namespace PoeOverlay.Core.Tests.Settings;

/// <summary>
/// S2 11.10 SE3 / SE3' / SE3" (S4 16.6) — who may clear a write block, and what happens when they do.
/// </summary>
public sealed class AcknowledgeTests
{
    [Fact]
    public async Task SE3_AcknowledgingACorruptFile_ResumesWritesAndPersistsTheEditsMadeMeanwhile()
    {
        using var harness = await SettingsHarness.StartedAsync("not json");
        Assert.Equal(WriteBlockReason.Corrupt, harness.Store.BlockReason);

        // Edits made while the banner was up are kept in memory and announced, but not written.
        harness.Store.Update(harness.Store.Current with { League = "Allflame", RefreshIntervalMinutes = 20 });
        Assert.False(File.Exists(harness.FilePath));

        harness.Store.Acknowledge();
        await harness.Store.AcknowledgeWrite.ConfigureAwait(false);

        // Clearing the banner without persisting would throw those edits away — the exact accident
        // D17 exists to prevent.
        Assert.Equal(WriteBlockReason.None, harness.Store.BlockReason);
        Assert.False(harness.Sink.StateOf(AppConditionKind.SettingsCorrupt));
        Assert.True(File.Exists(harness.FilePath));
        Assert.Contains("\"league\": \"Allflame\"", harness.ReadFile(), StringComparison.Ordinal);
        Assert.Contains("\"refreshIntervalMinutes\": 20", harness.ReadFile(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SE3Prime_AcknowledgingAnUnreadableFile_IsRefused()
    {
        using var harness = SettingsHarness.Create("""{ "schemaVersion": 1, "league": "Allflame" }""");

        using (var _ = new FileStream(harness.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await harness.Store.StartingAsync(CancellationToken.None);

            harness.Store.Update(harness.Store.Current with { League = "Standard" });
            harness.Store.Acknowledge();
            await harness.Store.AcknowledgeWrite.ConfigureAwait(false);

            // Allowing it would overwrite a file that could not be read — the very user data that
            // blocking writes was protecting. The block survives the request.
            Assert.Equal(WriteBlockReason.Unreadable, harness.Store.BlockReason);
            Assert.Equal(0, harness.Store.WriteCount);
        }

        Assert.Contains(harness.Logger.WithCode("AcknowledgeRefused"), e => e.Message.Contains("Unreadable", StringComparison.Ordinal));
        Assert.Equal("""{ "schemaVersion": 1, "league": "Allflame" }""", harness.ReadFile());
    }

    [Fact]
    public async Task SE3DoublePrime_AcknowledgingAFutureSchema_IsRefused()
    {
        using var harness = await SettingsHarness.StartedAsync("""{ "schemaVersion": 2, "league": "Allflame" }""");

        harness.Store.Acknowledge();
        await harness.Store.AcknowledgeWrite.ConfigureAwait(false);

        // Overwriting a newer file in an older format is the opposite of what read-only mode is for.
        Assert.Equal(WriteBlockReason.FutureSchema, harness.Store.BlockReason);
        Assert.Equal(0, harness.Store.WriteCount);
        Assert.Equal("""{ "schemaVersion": 2, "league": "Allflame" }""", harness.ReadFile());
    }

    [Fact]
    public async Task AcknowledgingWhenNothingIsBlocked_IsRefusedAndHarmless()
    {
        using var harness = await SettingsHarness.StartedAsync();

        harness.Store.Acknowledge();
        await harness.Store.AcknowledgeWrite.ConfigureAwait(false);

        Assert.Equal(WriteBlockReason.None, harness.Store.BlockReason);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task WhileWritesAreBlocked_UpdateStillMovesMemoryAndNotifies()
    {
        using var harness = await SettingsHarness.StartedAsync("not json");
        var notifications = 0;
        harness.Store.Changed += (_, _) => notifications++;

        harness.Store.Update(harness.Store.Current with { League = "Allflame" });
        harness.Store.Update(harness.Store.Current with { League = "Standard" });
        harness.Time.Advance(SettingsStore.DebounceWindow * 3);
        await harness.Store.FlushAsync(CancellationToken.None);

        // Only the disk write is skipped, and the fact is recorded once for the session rather than
        // once per keystroke.
        Assert.Equal("Standard", harness.Store.Current.League);
        Assert.Equal(2, notifications);
        Assert.Equal(0, harness.Store.WriteCount);
        Assert.Single(harness.Logger.WithCode("SettingsWriteBlocked"));
    }
}
