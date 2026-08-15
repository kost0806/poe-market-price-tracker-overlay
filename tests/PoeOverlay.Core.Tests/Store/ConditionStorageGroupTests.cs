using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Domain.Ports;
using PoeOverlay.Core.Store;
using Xunit;

namespace PoeOverlay.Core.Tests.Store;

/// <summary>
/// S2 11.8 S17 — the stored condition group really does contain the members the shell sets, and the
/// derived six really are refused (S2 2.11).
/// </summary>
public sealed class ConditionStorageGroupTests
{
    [Theory]
    [InlineData(AppConditionKind.LeagueUnresolved)]
    [InlineData(AppConditionKind.CommitRejected)]
    [InlineData(AppConditionKind.SettingsWriteFailed)]
    [InlineData(AppConditionKind.SettingsCorrupt)]
    [InlineData(AppConditionKind.SettingsReadOnly)]
    [InlineData(AppConditionKind.SettingsUnreadable)]
    [InlineData(AppConditionKind.TrayUnavailable)]
    [InlineData(AppConditionKind.LoggingUnavailable)]
    [InlineData(AppConditionKind.ViewModelRefreshFailing)]
    public async Task S17_EveryStoredConditionIsAccepted(AppConditionKind kind)
    {
        // ViewModelRefreshFailing is the load-bearing row: without it in the stored group, D-PS10
        // dies at runtime rather than at compile time.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.Set(kind, true, "detail");
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);

        var state = harness.Current.Conditions[kind];
        Assert.True(state.Active);
        Assert.Equal("detail", state.Detail);
        Assert.Equal(StoreTestHarness.Start, state.Since);
    }

    [Theory]
    [InlineData(AppConditionKind.FetchFailed)]
    [InlineData(AppConditionKind.RatePending)]
    [InlineData(AppConditionKind.RateInherited)]
    [InlineData(AppConditionKind.PollingStopped)]
    [InlineData(AppConditionKind.ItemUnresolved)]
    [InlineData(AppConditionKind.ItemDropped)]
    public async Task DerivedConditionsAreRefusedInReleaseToo(AppConditionKind kind)
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.Set(kind, true, null);
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);

        Assert.Empty(harness.Current.Conditions);
        var logged = Assert.Single(harness.Logger.WithCode(RejectionCodes.DerivedCondition));
        Assert.Equal(LogLevel.Warning, logged.Level);
    }

    [Fact]
    public async Task TheConditionSinkPortReachesTheSameStorage()
    {
        // D-C5: Settings and Shell know only the port. The Store is its one implementation.
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        IConditionSink conditions = harness.Store;
        IErrorSink errors = harness.Store;

        conditions.Set(AppConditionKind.SettingsWriteFailed, true, "denied");
        errors.Report(StoreTestHarness.Error("SettingsWriteFailed"));
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        Assert.True(harness.Current.Conditions[AppConditionKind.SettingsWriteFailed].Active);
        Assert.Equal("SettingsWriteFailed", harness.Current.LastError!.Code);
    }

    [Fact]
    public async Task SinceMarksTheTransitionRatherThanTheLatestSet()
    {
        using var harness = await StoreHarness.StartAsync().ConfigureAwait(false);

        harness.Store.Set(AppConditionKind.TrayUnavailable, true, "first");
        await harness.WaitForVersionAsync(1).ConfigureAwait(false);

        harness.Time.Advance(TimeSpan.FromMinutes(3));
        harness.Store.Set(AppConditionKind.TrayUnavailable, true, "second");
        await harness.WaitForVersionAsync(2).ConfigureAwait(false);

        var state = harness.Current.Conditions[AppConditionKind.TrayUnavailable];
        Assert.Equal(StoreTestHarness.Start, state.Since);
        Assert.Equal("second", state.Detail);

        harness.Store.Set(AppConditionKind.TrayUnavailable, false, null);
        await harness.WaitForVersionAsync(3).ConfigureAwait(false);
        Assert.Equal(StoreTestHarness.Start.AddMinutes(3), harness.Current.Conditions[AppConditionKind.TrayUnavailable].Since);
    }
}
