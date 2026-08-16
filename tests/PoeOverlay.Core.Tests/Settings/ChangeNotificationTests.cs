using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Settings;
using Xunit;

namespace PoeOverlay.Core.Tests.Settings;

/// <summary>
/// S2 11.10 SE11 / SE12 (S4 16.6) — when <c>Changed</c> fires.
/// </summary>
/// <remarks>
/// If <c>Watchlist</c> ever compares by reference, every save fires the event, Polling raises its
/// round generation, cancels the round in flight and schedules a repoll, and any path where a
/// repoll leads to a save closes the loop into infinite re-entry — with no compiler error anywhere.
/// </remarks>
public sealed class ChangeNotificationTests
{
    private static EquatableArray<WatchlistEntry> Watchlist(params string[] ids)
        => new(ids.Select(id => new WatchlistEntry(
            new ItemId(id), new CategoryRef("Currency", ExchangeCategory.Currency), null)));

    [Fact]
    public async Task SE11_UpdatingWithAnEqualButDistinctArray_DoesNotFire()
    {
        using var harness = await SettingsHarness.StartedAsync();
        harness.Store.Update(harness.Store.Current with { Watchlist = Watchlist("divine", "chaos") });

        var fired = 0;
        harness.Store.Changed += (_, _) => fired++;

        harness.Store.Update(harness.Store.Current with { Watchlist = Watchlist("divine", "chaos") });

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task SE12_UpdatingWithOneDifferentEntry_Fires()
    {
        using var harness = await SettingsHarness.StartedAsync();
        harness.Store.Update(harness.Store.Current with { Watchlist = Watchlist("divine", "chaos") });

        AppSettings? seenOld = null;
        AppSettings? seenNew = null;
        harness.Store.Changed += (o, n) => (seenOld, seenNew) = (o, n);

        harness.Store.Update(harness.Store.Current with { Watchlist = Watchlist("divine", "mirror") });

        Assert.NotNull(seenOld);
        Assert.NotNull(seenNew);

        // Both values are carried so each consumer can diff only the keys it cares about.
        Assert.Equal(new ItemId("chaos"), seenOld.Watchlist[1].Id);
        Assert.Equal(new ItemId("mirror"), seenNew.Watchlist[1].Id);
    }

    [Fact]
    public async Task TheNewValueIsPublishedBeforeTheNotification()
    {
        using var harness = await SettingsHarness.StartedAsync();
        string? currentSeenByHandler = null;
        harness.Store.Changed += (_, _) => currentSeenByHandler = harness.Store.Current.League;

        harness.Store.Update(harness.Store.Current with { League = "Allflame" });

        // A handler that read Current and saw the old value would act on a state that no longer
        // exists by the time it finishes.
        Assert.Equal("Allflame", currentSeenByHandler);
    }

    [Fact]
    public async Task AnUnchangedValue_NeitherFiresNorSchedulesAWrite()
    {
        using var harness = await SettingsHarness.StartedAsync();
        var fired = 0;
        harness.Store.Changed += (_, _) => fired++;

        harness.Store.Update(harness.Store.Current);
        harness.Time.Advance(SettingsStore.DebounceWindow * 3);
        await harness.Store.FlushAsync(CancellationToken.None);

        Assert.Equal(0, fired);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public void UpdateTakesAValueRatherThanADelegate()
    {
        var parameter = Assert.Single(typeof(ISettingsSource).GetMethod(nameof(ISettingsSource.Update))!.GetParameters());

        // The type is the enforcement. A Func<AppSettings, AppSettings> could read the live window
        // inside itself, and while SizeToContent is active the window's Height is whatever the last
        // layout pass produced rather than what the user chose (D19).
        Assert.Equal(typeof(AppSettings), parameter.ParameterType);
    }
}
