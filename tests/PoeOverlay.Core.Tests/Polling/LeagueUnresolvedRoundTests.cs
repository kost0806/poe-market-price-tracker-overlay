using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Polling;

/// <summary>
/// <c>RoundOutcome.LeagueUnresolved</c> must not be read as a run of rejected commits.
/// </summary>
/// <remarks>
/// A round that ends before a league is settled makes no request and issues no commit. Counting it
/// as a commit-free round means two of them raise CommitRejected — carrying a stale detail from
/// some earlier round — on top of the LeagueUnresolved condition that is already saying the true
/// thing, telling the user their data is being rejected when in fact nobody has decided which
/// league to ask about. It is the same conflation cancellation had; the member was unreachable
/// until Polling existed to produce it.
/// </remarks>
public sealed class LeagueUnresolvedRoundTests
{
    private static async Task<PollingHarness> UnresolvableAsync()
    {
        var harness = await PollingHarness.CreateAsync(PollingTestHarness.Settings(league: null))
            .ConfigureAwait(false);
        harness.Market.Leagues = new LeagueList(
            [], PollingTestHarness.Start, LeagueListStatus.Failed, "EmptyLeagueList");
        return harness;
    }

    [Fact]
    public async Task TwoUnresolvedRoundsInARow_DoNotRaiseCommitRejected()
    {
        using var harness = await UnresolvableAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);

        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(
            s => s.Heartbeat.LastRoundNumber == 2 && s.Heartbeat.LastOutcome is not null, "both rounds recorded");

        var snapshot = harness.Current;
        Assert.All(harness.Rounds, r => Assert.Equal(RoundOutcome.LeagueUnresolved, r.Outcome));

        // Reverting the exemption makes this two, and the condition below active.
        Assert.Equal(0, snapshot.ConsecutiveEmptyCommitRounds);
        Assert.False(
            snapshot.Conditions.TryGetValue(AppConditionKind.CommitRejected, out var rejected) && rejected.Active);

        // The condition that is true is raised, with a reason the banner can show.
        Assert.True(snapshot.Conditions[AppConditionKind.LeagueUnresolved].Active);
        Assert.Equal("EmptyLeagueList", snapshot.Conditions[AppConditionKind.LeagueUnresolved].Detail);
        Assert.Equal(LeagueResolutionState.Unresolved, snapshot.LeagueResolution.State);
    }

    [Fact]
    public async Task AFailedLeagueList_IsStillCommittedAndReported()
    {
        using var harness = await UnresolvableAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(s => s.Leagues is not null, "the verdict was committed");

        // A failed list is still information the settings window needs, or the user cannot even be
        // offered the manual entry that would break the deadlock.
        Assert.Equal(LeagueListStatus.Failed, harness.Current.Leagues!.Status);
        Assert.Equal("EmptyLeagueList", harness.Current.LastError!.Code);
        Assert.Equal("ui.error.leagueListInvalid", harness.Current.LastError.MessageKey);
    }

    [Fact]
    public async Task OnceALeagueIsSettled_TheConditionClears()
    {
        using var harness = await UnresolvableAsync();
        await harness.StartAsync();
        await harness.RunRoundAsync(1);
        await harness.WaitForAsync(
            s => s.Conditions.TryGetValue(AppConditionKind.LeagueUnresolved, out var c) && c.Active,
            "the condition was raised");

        harness.Market.Leagues = new LeagueList(
            [new LeagueEntry("Allflame", "Allflame")], PollingTestHarness.Start, LeagueListStatus.Ok, null);

        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.RunRoundAsync(2);
        await harness.WaitForAsync(s => s.DataLeague == "Allflame", "the league was settled");

        Assert.False(harness.Current.Conditions[AppConditionKind.LeagueUnresolved].Active);
        Assert.Equal(LeagueResolutionState.Resolved, harness.Current.LeagueResolution.State);
    }
}
