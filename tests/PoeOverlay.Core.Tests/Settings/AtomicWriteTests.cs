using System.Text.Json;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Settings;
using Xunit;

namespace PoeOverlay.Core.Tests.Settings;

/// <summary>
/// S2 11.10 SE14 – SE18 (S4 16.6) — the atomic write, its debounce and its retries.
/// </summary>
public sealed class AtomicWriteTests
{
    [Fact]
    public async Task SE14_AfterAWrite_NoIntermediateFileIsLeftAndTheDocumentIsComplete()
    {
        using var harness = await SettingsHarness.StartedAsync();
        harness.Store.Update(harness.Store.Current with { League = "Allflame" });
        await harness.Store.FlushAsync(CancellationToken.None);

        Assert.False(File.Exists(harness.TempPath));

        // The rename is the commit point, so what is on disk is either the old document or a whole
        // new one — never a half-written one.
        using var document = JsonDocument.Parse(harness.ReadFile());
        Assert.Equal("Allflame", document.RootElement.GetProperty("league").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task AnAbandonedTempFile_DoesNotAffectTheNextWrite()
    {
        using var harness = await SettingsHarness.StartedAsync();
        File.WriteAllText(harness.TempPath, "{ leftover from a killed process");

        harness.Store.Update(harness.Store.Current with { League = "Allflame" });
        await harness.Store.FlushAsync(CancellationToken.None);

        Assert.False(File.Exists(harness.TempPath));
        Assert.Equal("Allflame", JsonDocument.Parse(harness.ReadFile()).RootElement.GetProperty("league").GetString());
    }

    [Fact]
    public async Task SE15_TheSecondWrite_LeavesThePreviousDocumentAsTheBackup()
    {
        using var harness = await SettingsHarness.StartedAsync();

        harness.Store.Update(harness.Store.Current with { League = "Allflame" });
        await harness.Store.FlushAsync(CancellationToken.None);
        Assert.False(File.Exists(harness.BackupPath));

        harness.Store.Update(harness.Store.Current with { League = "Standard" });
        await harness.Store.FlushAsync(CancellationToken.None);

        // File.Replace takes the backup name as an argument, so replace-and-back-up is one call in
        // one directory. The backup therefore holds the last file successfully *written*, which is
        // a stronger guarantee than "last successfully loaded": it is known to load.
        Assert.Equal(
            "Allflame",
            JsonDocument.Parse(File.ReadAllText(harness.BackupPath)).RootElement.GetProperty("league").GetString());
        Assert.Equal(
            "Standard",
            JsonDocument.Parse(harness.ReadFile()).RootElement.GetProperty("league").GetString());
    }

    [Fact]
    public async Task SE16_ThreeUpdatesInsideTheWindow_ProduceOneWriteOfTheLastValue()
    {
        using var harness = await SettingsHarness.StartedAsync();

        foreach (var height in new[] { 500d, 640d, 720d })
        {
            harness.Store.Update(harness.Store.Current with
            {
                Window = harness.Store.Current.Window with { Height = height },
            });

            harness.Time.Advance(TimeSpan.FromMilliseconds(200));
        }

        harness.Time.Advance(SettingsStore.DebounceWindow);
        await harness.Store.FlushAsync(CancellationToken.None);

        Assert.Equal(1, harness.Store.WriteCount);
        Assert.Equal(
            720d,
            JsonDocument.Parse(harness.ReadFile()).RootElement.GetProperty("window").GetProperty("height").GetDouble());
    }

    [Fact]
    public async Task SE17_FlushingTwiceWithNothingPending_WritesNothing()
    {
        using var harness = await SettingsHarness.StartedAsync();

        await harness.Store.FlushAsync(CancellationToken.None);
        await harness.Store.FlushAsync(CancellationToken.None);

        Assert.Equal(0, harness.Store.WriteCount);
        Assert.False(File.Exists(harness.FilePath));

        harness.Store.Update(harness.Store.Current with { League = "Allflame" });
        await harness.Store.FlushAsync(CancellationToken.None);
        await harness.Store.FlushAsync(CancellationToken.None);

        // Idempotent: the second flush finds nothing pending and returns without touching the disk.
        Assert.Equal(1, harness.Store.WriteCount);
    }

    [Fact]
    public async Task SE18_ALockedFile_RetriesAndThenReportsAFailureThatOnlyASuccessClears()
    {
        using var harness = await SettingsHarness.StartedAsync();

        harness.Store.Update(harness.Store.Current with { League = "Allflame" });
        await harness.Store.FlushAsync(CancellationToken.None);
        Assert.False(harness.Store.LastWriteFailed);

        var attemptsBefore = harness.Store.WriteAttempts;
        harness.Store.Update(harness.Store.Current with { League = "Standard" });

        using (var _ = new FileStream(harness.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // The backoff runs on the injected clock, so the retries need the clock to move.
            await harness.AdvanceUntilCompleteAsync(harness.Store.FlushAsync(CancellationToken.None));

            Assert.True(harness.Store.LastWriteFailed);
            Assert.Equal(SettingsStore.WriteRetryDelays.Length + 1, harness.Store.WriteAttempts - attemptsBefore);
            Assert.True(harness.Sink.StateOf(AppConditionKind.SettingsWriteFailed));
            Assert.Equal("ui.error.settingsWriteFailed", harness.Sink.Errors[^1].MessageKey);
            Assert.False(File.Exists(harness.TempPath));
        }

        // There is no acknowledge for this one: claiming a save that never happened is the failure
        // mode the condition exists to prevent. Only a real write clears it.
        harness.Store.Update(harness.Store.Current with { League = "Hardcore" });
        await harness.Store.FlushAsync(CancellationToken.None);

        Assert.False(harness.Store.LastWriteFailed);
        Assert.False(harness.Sink.StateOf(AppConditionKind.SettingsWriteFailed));
        Assert.Equal("Hardcore", JsonDocument.Parse(harness.ReadFile()).RootElement.GetProperty("league").GetString());
    }

    [Fact]
    public async Task TheDebounceTimer_WritesWithoutAnExplicitFlush()
    {
        using var harness = await SettingsHarness.StartedAsync();
        harness.Store.Update(harness.Store.Current with { League = "Allflame" });

        harness.Time.Advance(SettingsStore.DebounceWindow);
        await harness.AdvanceUntilCompleteAsync(harness.Store.FlushAsync(CancellationToken.None));

        Assert.Equal(1, harness.Store.WriteCount);
    }

    [Fact]
    public async Task AFailedShutdownFlush_LeavesABreadcrumbTheNextStartUpReportsOnce()
    {
        using var harness = await SettingsHarness.StartedAsync();
        harness.Store.Update(harness.Store.Current with { League = "Allflame" });
        await harness.Store.FlushAsync(CancellationToken.None);

        harness.Store.Update(harness.Store.Current with { League = "Standard" });

        using (var _ = new FileStream(harness.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await harness.AdvanceUntilCompleteAsync(harness.Store.StoppingAsync(CancellationToken.None));
        }

        var tracePath = Path.Combine(harness.Directory, SettingsStore.FlushFailureTraceFileName);
        Assert.True(File.Exists(tracePath));
        Assert.Contains("2026-08-16T07:00", File.ReadAllText(tracePath), StringComparison.Ordinal);

        // The next start-up reports it exactly once and removes it, so the warning tracks the last
        // shutdown rather than every shutdown since the first failure.
        var next = new SettingsStore(harness.Directory, harness.Time, harness.Sink, harness.Sink, harness.Logger);
        await next.StartingAsync(CancellationToken.None);
        await next.StoppedAsync(CancellationToken.None);

        Assert.False(File.Exists(tracePath));
        Assert.Single(harness.Logger.WithCode("SettingsFlushFailureTrace"));
    }
}
