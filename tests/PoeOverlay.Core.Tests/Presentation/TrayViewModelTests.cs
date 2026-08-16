using Microsoft.Extensions.Time.Testing;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Tests.TestSupport;
using Xunit;

namespace PoeOverlay.Core.Tests.Presentation;

/// <summary>HLD D21 — three variants, worst-first tooltip assembly, and the length ceiling.</summary>
public sealed class TrayViewModelTests
{
    private static readonly DateTimeOffset Now = SnapshotBuilder.Now;

    [Fact]
    public void IconIsNormal_WhenNothingIsWrong()
    {
        var (vm, _) = Build();

        vm.Refresh(Healthy(), Now);

        Assert.Equal(TrayIconVariant.Normal, vm.IconVariant);
        Assert.Equal("PoE Market Price Tracker", vm.TooltipText);
    }

    [Fact]
    public void IconIsWarning_ForSomethingThatMayRecoverOnItsOwn()
    {
        var (vm, _) = Build();

        var snapshot = Healthy() with
        {
            CategoryStatuses = new Dictionary<ExchangeCategory, CategoryStatus>
            {
                [ExchangeCategory.Currency] = new(
                    ExchangeCategory.Currency, 1, Now, null, null, null, 0, null, false),
            },
        };

        vm.Refresh(snapshot, Now);

        // Two variants would collapse "wait a minute" and "do something" into one signal (D21).
        Assert.Equal(TrayIconVariant.Warning, vm.IconVariant);
    }

    [Fact]
    public void IconIsError_WhenTheUserHasToAct()
    {
        var (vm, _) = Build();

        var snapshot = SnapshotBuilder.WithConditions((AppConditionKind.SettingsCorrupt, null)) with
        {
            Rate = Rate(),
        };

        vm.Refresh(snapshot, Now);

        Assert.Equal(TrayIconVariant.Error, vm.IconVariant);
    }

    [Fact]
    public void APendingRate_DoesNotDisturbTheIcon()
    {
        var (vm, _) = Build();

        // The rate is pending on a cold start; an icon that is abnormal most of the time stops
        // being a signal.
        vm.Refresh(SnapshotBuilder.Empty(), Now);

        Assert.Equal(TrayIconVariant.Normal, vm.IconVariant);
    }

    [Fact]
    public void Tooltip_NeverExceedsTheWinFormsCeiling()
    {
        var (vm, _) = Build();

        var snapshot = SnapshotBuilder.WithConditions(
            (AppConditionKind.SettingsCorrupt, null),
            (AppConditionKind.TrayUnavailable, null),
            (AppConditionKind.LeagueUnresolved, null),
            (AppConditionKind.CommitRejected, null),
            (AppConditionKind.SettingsWriteFailed, null)) with { Rate = Rate() };

        vm.Refresh(snapshot, Now);

        // NotifyIcon.Text throws above its limit, and a throw on the UI thread under an empty
        // allow-list ends the process (D-SH13). The assembler cuts instead.
        Assert.True(
            vm.TooltipText.Length <= TrayViewModel.TooltipMaxLength,
            $"tooltip was {vm.TooltipText.Length} characters: {vm.TooltipText}");
    }

    [Fact]
    public void Tooltip_LeadsWithTheWorstState_AndCountsTheRest()
    {
        var (vm, _) = Build();

        var snapshot = SnapshotBuilder.WithConditions(
            (AppConditionKind.SettingsCorrupt, null),
            (AppConditionKind.TrayUnavailable, null),
            (AppConditionKind.LeagueUnresolved, null)) with { Rate = Rate() };

        vm.Refresh(snapshot, Now);

        // The half worth reading is the summary, so the app name is what gets dropped (D21).
        Assert.StartsWith("settings file was corrupted", vm.TooltipText, StringComparison.Ordinal);
        Assert.Contains("more)", vm.TooltipText, StringComparison.Ordinal);
        Assert.DoesNotContain("PoE Market Price Tracker", vm.TooltipText, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveMode_IsAMenuItem_NotAFourthIconVariant()
    {
        var (vm, moveMode) = Build();
        vm.Refresh(Healthy(), Now);

        moveMode.EnterMoveMode();

        Assert.True(vm.ShowMoveModeOffMenuItem);
        Assert.Equal(TrayIconVariant.Normal, vm.IconVariant);

        moveMode.ExitMoveMode(MoveModeExitReason.TrayMenu);
        Assert.False(vm.ShowMoveModeOffMenuItem);
    }

    private static DivineRate Rate() => new(200m, Now, SnapshotBuilder.League, false);

    private static MarketSnapshot Healthy()
        => SnapshotBuilder.Empty() with
        {
            Rate = Rate(),
            Heartbeat = new Heartbeat(Now, 2, Now, RoundOutcome.Completed, false, null, null),
        };

    private static (TrayViewModel Vm, FakeOverlayModeService MoveMode) Build()
    {
        var moveMode = new FakeOverlayModeService();
        var vm = new TrayViewModel(
            new FakeLocalizer(),
            moveMode,
            new FakeSettingsSource(),
            new FakeTimeProvider(Now),
            new RecordingLogger<TrayViewModel>());

        return (vm, moveMode);
    }
}
