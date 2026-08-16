using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Store;

/// <summary>
/// S2 11.8 S6, S7, S7′, S7″ — loop survival, the last trace of a refused command, and the shutdown
/// drain (S2 6.3 / 6.6).
/// </summary>
public sealed class ApplyFaultTests
{
    [Fact]
    public async Task S6_AnApplyThatThrows_KeepsTheLoopAliveAndUpdatesLastError()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        var handled = 0;
        void Explode(object? sender, EventArgs e)
        {
            if (Interlocked.Increment(ref handled) == 1)
            {
                throw new InvalidOperationException("boom");
            }
        }

        harness.Store.SnapshotChanged += Explode;

        harness.Store.RecordHeartbeatAttempt(1);

        // Version 1 is the faulting publish; version 2 is the lastError the same catch queues.
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);
        harness.Store.SnapshotChanged -= Explode;

        var error = harness.Current.LastError;
        Assert.NotNull(error);
        Assert.Equal("Store", error.Module);
        Assert.Equal("ApplyFault", error.Code);
        Assert.Equal("ui.error.applyFault", error.MessageKey);
        Assert.Equal("InvalidOperationException", error.ExceptionType);

        var logged = Assert.Single(harness.Logger.WithCode("ApplyFault"));
        Assert.Equal(LogLevel.Error, logged.Level);

        // Survival alone is not enough — but survival is still required.
        harness.Store.RecordHeartbeatOutcome(RoundOutcome.Completed);
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);
        Assert.Equal(RoundOutcome.Completed, harness.Current.Heartbeat.LastOutcome);
    }

    [Fact]
    public async Task S6Prime_TheFaultRecordCarriesTheCategoryAndLeagueItHas()
    {
        // This is the one channel designed to survive Apply throwing, so it carries everything the
        // store can see. RoundNumber stays null by construction (D-ST1), not by omission.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);

        var handled = 0;
        void Explode(object? sender, EventArgs e)
        {
            if (Interlocked.Increment(ref handled) == 1)
            {
                throw new InvalidOperationException("boom");
            }
        }

        harness.Store.SnapshotChanged += Explode;
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());

        // Version 2 is the faulting publish; version 3 is the lastError the same catch queues.
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);
        harness.Store.SnapshotChanged -= Explode;

        var error = harness.Current.LastError;
        Assert.NotNull(error);
        Assert.Equal("ApplyFault", error.Code);
        Assert.Equal(nameof(ExchangeCategory.Currency), error.Category);
        Assert.Equal(StoreTestHarness.League, error.League);
        Assert.Null(error.RoundNumber);
    }

    [Fact]
    public async Task S7_ACommitAfterTheLoopExitRecord_StillApplies()
    {
        // RecordLoopExit describes Polling's loop, not the store's. The store outlives it, which is
        // the whole reason the consumer loop was moved here.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.RecordLoopExit(LoopExitKind.Faulted);
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);

        Assert.True(harness.Current.Heartbeat.LoopExited);
        Assert.Equal(LoopExitKind.Faulted, harness.Current.Heartbeat.ExitKind);
        Assert.Single(harness.Current.Categories);
    }

    [Fact]
    public async Task S7Prime_PostingAfterCompleteIsRecordedAsAnError()
    {
        // Measured: TryWrite returns false once the writer is completed, and the first edition
        // assumed an unbounded channel always accepts. The log line is the command's last trace.
        var harness = await StoreHarness.StartAsync().ConfigureAwait(false);
        using (harness)
        {
            harness.Store.RecordHeartbeatAttempt(1);
            await harness.WaitForVersionAsync(1).ConfigureAwait(false);
            await harness.StopAsync().ConfigureAwait(false);

            harness.Store.RecordHeartbeatAttempt(2);

            var logged = Assert.Single(harness.Logger.WithCode("PostAfterComplete"));
            Assert.Equal(LogLevel.Error, logged.Level);
            Assert.Equal(1, harness.Current.Version);
            Assert.Equal(1, harness.Current.Heartbeat.LastRoundNumber);
        }
    }

    [Fact]
    public async Task S7DoublePrime_FiveBufferedCommands_AreAllAppliedBeforeStopReturns()
    {
        // Measured: ReadAllAsync with an already-cancelled token drains none of five buffered
        // commands. StopAsync therefore completes the writer and waits without cancelling; the
        // token is a hard timeout only.
        var harness = await StoreHarness.StartAsync().ConfigureAwait(false);
        using (harness)
        {
            using var gate = new ManualResetEventSlim(false);
            using var blocked = new ManualResetEventSlim(false);

            var first = true;
            void Hold(object? sender, EventArgs e)
            {
                if (!first)
                {
                    return;
                }

                first = false;
                blocked.Set();
                gate.Wait(TimeSpan.FromSeconds(5));
            }

            harness.Store.SnapshotChanged += Hold;

            // Block the consumer inside its first publish.
            harness.Store.RecordHeartbeatAttempt(1);
            Assert.True(blocked.Wait(TimeSpan.FromSeconds(5)), "The consumer never reached the first publish.");

            for (var i = 0; i < 5; i++)
            {
                harness.Store.RecordHeartbeatAttempt(i + 2);
            }

            var stopping = harness.StopAsync();

            // Completing the writer must not end the wait: the loop is still holding five commands.
            var settled = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromMilliseconds(100)))
                .ConfigureAwait(false);
            Assert.NotSame(stopping, settled);

            gate.Set();
            await stopping.ConfigureAwait(false);

            harness.Store.SnapshotChanged -= Hold;

            Assert.Equal(6, harness.Current.Version);
            Assert.Equal(6, harness.Current.Heartbeat.LastRoundNumber);
        }
    }

    [Fact]
    public async Task S18_AnOrdinaryShutdown_RecordsTheLoopExitWithoutClaimingAnError()
    {
        // Every clean run ended with an [ERR] line, which is exactly the line an operator scans a
        // log for. The record itself is kept — the loop leaving is worth knowing either way — but
        // the level now says which of the two exits happened (S2 6.3).
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.RecordHeartbeatAttempt(1);
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);
        await harness.StopAsync().ConfigureAwait(false);

        var exits = harness.Logger.WithCode("LoopExited");
        var exit = Assert.Single(exits);

        Assert.Equal(LogLevel.Information, exit.Level);
        Assert.DoesNotContain(harness.Logger.Entries, e => e.Level >= LogLevel.Error);
    }
}
