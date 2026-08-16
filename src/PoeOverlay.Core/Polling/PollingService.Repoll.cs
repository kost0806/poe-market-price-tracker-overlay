using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Core.Polling;

/// <summary>
/// The settings-change diff and the repoll debounce (S2 7.7 D11, S4 9.3).
/// </summary>
public sealed partial class PollingService
{
    private int _repollPending;

    /// <summary>
    /// Applies the S2 7.7 diff table.
    /// </summary>
    /// <remarks>
    /// The league comparison trims both sides, exactly as round step 4 does. Comparing untrimmed
    /// values here and trimmed values there would make <c>"Allflame "</c> → <c>"Allflame"</c> look
    /// like a league change that then resolves to the same league.
    /// </remarks>
    private void OnSettingsChanged(AppSettings oldSettings, AppSettings newSettings)
    {
        ArgumentNullException.ThrowIfNull(oldSettings);
        ArgumentNullException.ThrowIfNull(newSettings);

        if (oldSettings.RefreshIntervalMinutes != newSettings.RefreshIntervalMinutes)
        {
            // Neither counter moves and the round in flight is not cancelled: the interval says how
            // often to ask, not what world the data belongs to.
            SetPeriod(newSettings.RefreshIntervalMinutes);
        }

        var leagueChanged = !string.Equals(
            oldSettings.League?.Trim(), newSettings.League?.Trim(), StringComparison.Ordinal);

        if (leagueChanged)
        {
            Interlocked.Exchange(ref _pendingLeagueChangeTrigger, 1);
            Interlocked.Increment(ref _roundGeneration);
            CancelRound();
            ScheduleRepoll();
            return;
        }

        if (oldSettings.Watchlist.Equals(newSettings.Watchlist))
        {
            return;
        }

        // The round in flight is asking about a watchlist that no longer exists, so it is cancelled;
        // the epoch does not move, because the data world did not change and the snapshots already
        // committed are still current (INV-7).
        Interlocked.Increment(ref _roundGeneration);
        CancelRound();

        if (RequiresImmediateRepoll(oldSettings, newSettings, _store.Current.Categories))
        {
            ScheduleRepoll();
        }
    }

    /// <summary>
    /// Whether a watchlist edit needs data the store does not already hold (S2 7.7).
    /// </summary>
    /// <remarks>
    /// Adding an item in a category that is already cached needs no request — the row can render
    /// immediately — and removals never need one. Repolling for either would turn ordinary editing
    /// into traffic (NFR-02).
    /// </remarks>
    internal static bool RequiresImmediateRepoll(
        AppSettings oldSettings,
        AppSettings newSettings,
        IReadOnlyDictionary<ExchangeCategory, CategorySnapshot> currentCategories)
    {
        ArgumentNullException.ThrowIfNull(oldSettings);
        ArgumentNullException.ThrowIfNull(newSettings);
        ArgumentNullException.ThrowIfNull(currentCategories);

        var before = new HashSet<ExchangeCategory>();
        foreach (var entry in oldSettings.Watchlist)
        {
            if (entry.Category.Known is { } known)
            {
                before.Add(known);
            }
        }

        foreach (var entry in newSettings.Watchlist)
        {
            if (entry.Category.Known is { } known
                && !before.Contains(known)
                && !currentCategories.ContainsKey(known))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Arms the debounce window.
    /// </summary>
    /// <remarks>
    /// The request is remembered in a flag as well as in the timer, so that the floor check below
    /// can re-arm without losing it: S2 7.7 says a request that arrives too soon is delayed, never
    /// dropped, and dropping it is precisely how an edit becomes a no-op.
    /// </remarks>
    private void ScheduleRepoll()
    {
        Interlocked.Exchange(ref _repollPending, 1);
        ChangeRepollTimer(PollingOptions.RepollDebounceWindow);
    }

    private void OnRepollTimerElapsed()
    {
        if (Volatile.Read(ref _repollPending) == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (_lastRoundCompletedAt is { } completed)
        {
            var earliest = completed + PollingOptions.MinimumRepollSpacing;
            if (now < earliest)
            {
                ChangeRepollTimer(earliest - now);
                return;
            }
        }

        Interlocked.Exchange(ref _repollPending, 0);

        // Queued rather than run: a repoll never nests inside the round in flight. If a round is
        // running, the trigger simply waits in the channel and the loop picks it up on the next
        // pass.
        Post(PollingTriggerKind.Repoll);
    }

    private void ChangeRepollTimer(TimeSpan due)
    {
        try
        {
            _repollTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            Log(LogLevel.Debug, "RepollTimerDisposed", "A repoll was requested after shutdown began.");
        }
    }
}
