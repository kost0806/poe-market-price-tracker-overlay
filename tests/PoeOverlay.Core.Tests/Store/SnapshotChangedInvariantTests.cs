using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Store;
using Xunit;

namespace PoeOverlay.Core.Tests.Store;

/// <summary>
/// S2 11.8 S16 — every applied command produces exactly one snapshot and exactly one signal.
/// </summary>
/// <remarks>
/// The AP → EV edge of S2 6.3 with no exceptions, which is what S3 8.4's re-entrancy argument
/// stands on: <c>Set</c> and <c>Report</c> publish too, so a condition raised inside a publish pass
/// is a second publish and cannot be exempted from the guard.
/// </remarks>
public sealed class SnapshotChangedInvariantTests
{
    [Theory]
    [InlineData("BeginNewLeague")]
    [InlineData("SetLeagueUnresolved")]
    [InlineData("SetLeagueList")]
    [InlineData("RecordHeartbeatAttempt")]
    [InlineData("RecordHeartbeatOutcome")]
    [InlineData("RecordLoopExit")]
    [InlineData("SetLastError")]
    [InlineData("SetCondition")]
    [InlineData("RejectedCommit")]
    [InlineData("RejectedDerivedCondition")]
    public async Task S16_EveryCommandSignalsExactlyOnce(string command)
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        Issue(harness, command);
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);

        Assert.Equal(1, harness.Current.Version);
        Assert.Equal(1, harness.Events);
    }

    [Fact]
    public async Task S16_ASequenceOfCommandsPublishesOneVersionEach()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
        harness.Store.Set(AppConditionKind.LoggingUnavailable, true, "C:/logs");
        harness.Store.Report(StoreTestHarness.Error());
        harness.Store.CommitCategory(StoreTestHarness.Tag, StoreTestHarness.Snapshot());
        harness.Store.CommitCategory(default, StoreTestHarness.Snapshot());

        await harness.WaitForVersionAsync(5).ConfigureAwait(false);

        Assert.Equal(5, harness.Current.Version);
        Assert.Equal(5, harness.Events);
    }

    private static void Issue(StoreHarness harness, string command)
    {
        switch (command)
        {
            case "BeginNewLeague":
                harness.Store.BeginNewLeague(StoreTestHarness.League, 1);
                break;
            case "SetLeagueUnresolved":
                harness.Store.SetLeagueUnresolved("Suspicious");
                break;
            case "SetLeagueList":
                harness.Store.SetLeagueList(
                    new LeagueList([], StoreTestHarness.Start, LeagueListStatus.Failed, "EmptyLeagueList"));
                break;
            case "RecordHeartbeatAttempt":
                harness.Store.RecordHeartbeatAttempt(4);
                break;
            case "RecordHeartbeatOutcome":
                harness.Store.RecordHeartbeatOutcome(RoundOutcome.PartiallyFailed);
                break;
            case "RecordLoopExit":
                harness.Store.RecordLoopExit(LoopExitKind.Canceled);
                break;
            case "SetLastError":
                harness.Store.Report(StoreTestHarness.Error());
                break;
            case "SetCondition":
                harness.Store.Set(AppConditionKind.TrayUnavailable, true, "registration failed");
                break;
            case "RejectedCommit":
                harness.Store.CommitCategory(default, StoreTestHarness.Snapshot());
                break;
            case "RejectedDerivedCondition":
                harness.Store.Set(AppConditionKind.RatePending, true, null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown command.");
        }
    }
}
