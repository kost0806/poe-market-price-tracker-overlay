using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Pricing;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Store;

namespace PoeOverlay.Core.Polling;

/// <summary>
/// The thirteen steps of one round (S2 7.3 – 7.8, S4 9.2).
/// </summary>
public sealed partial class PollingService
{
    /// <summary>The one line the divine rate is read from (contract 3.1); the reciprocal in <c>core.rates</c> is forbidden.</summary>
    internal static readonly ItemId DivineId = new("divine");

    /// <summary>S2 7.3 — the league list was suspicious and no league was configured, so nothing was settled.</summary>
    internal const string SuspiciousLeagueListReason = "SuspiciousLeagueList";

    private static readonly FrozenDictionary<ExchangeCategory, CategorySnapshot> NoCategories =
        new Dictionary<ExchangeCategory, CategorySnapshot>().ToFrozenDictionary();

    private static readonly FrozenDictionary<ExchangeCategory, CategoryStatus> NoStatuses =
        new Dictionary<ExchangeCategory, CategoryStatus>().ToFrozenDictionary();

    /// <summary>
    /// Runs one round and records its heartbeat.
    /// </summary>
    /// <remarks>
    /// The attempt is recorded before anything that can return early — before the settings snapshot,
    /// before the league list, before any validation (D20). A round that dies in its first step
    /// still proves the loop is alive, and without that the stall verdict would accuse a loop that
    /// is running perfectly well and merely failing.
    /// </remarks>
    private async Task RunRoundAsync(RoundTrigger trigger, CancellationToken stoppingToken)
    {
        var roundNumber = Interlocked.Increment(ref _roundNumber);
        _store.RecordHeartbeatAttempt(roundNumber);
        Log(LogLevel.Information, "RoundStarted", $"Round {roundNumber.ToString(CultureInfo.InvariantCulture)} started ({trigger}).");

        var settings = _settings.Current;

        using var roundCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_roundGate)
        {
            _roundCts = roundCts;
        }

        RoundOutcome outcome;
        try
        {
            outcome = await ExecuteRoundStepsAsync(trigger, settings, roundNumber, roundCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // A round-scoped cancellation is an edit, not a fault and not a rejection. Recording the
            // outcome matters: without it LastRoundCompletedAt would stall in an edit-heavy session
            // and the stall verdict would fire against a healthy loop (S2 7.8).
            outcome = RoundOutcome.Canceled;
        }
        finally
        {
            lock (_roundGate)
            {
                if (ReferenceEquals(_roundCts, roundCts))
                {
                    _roundCts = null;
                }
            }
        }

        _store.RecordHeartbeatOutcome(outcome);
        _lastRoundCompletedAt = _timeProvider.GetUtcNow();

        // Step 13. Re-read the settings rather than reusing the round's snapshot: the interval may
        // have changed mid-round, and that change deliberately does not cancel the round.
        SetPeriod(_settings.Current.RefreshIntervalMinutes);

        RoundCompleted?.Invoke(roundNumber, outcome);
    }

    private async Task<RoundOutcome> ExecuteRoundStepsAsync(
        RoundTrigger trigger,
        AppSettings settings,
        int roundNumber,
        CancellationToken ct)
    {
        var before = _store.Current;

        // Step 3. The verdict is committed whatever it is: a failed list is still information the
        // settings window needs in order to offer the manual league entry.
        var leagues = await FetchLeagueListAsync(ct).ConfigureAwait(false);
        _store.SetLeagueList(leagues);

        if (leagues.Status == LeagueListStatus.Failed)
        {
            _store.Report(new ErrorRecord(
                _timeProvider.GetUtcNow(),
                "Polling",
                leagues.FailureCode ?? "LeagueListInvalid",
                "ui.error.leagueListInvalid",
                null,
                null,
                null,
                roundNumber,
                null));
        }

        // Step 4.
        var (state, league, reasonCode) = ResolveLeague(settings.League, leagues);
        if (state != LeagueResolutionState.Resolved || league is null)
        {
            _store.SetLeagueUnresolved(reasonCode ?? "LeagueUnresolved");
            _store.Set(AppConditionKind.LeagueUnresolved, true, reasonCode);
            Log(LogLevel.Warning, "LeagueUnresolved", $"Round {roundNumber.ToString(CultureInfo.InvariantCulture)} could not settle on a league ({reasonCode}).");

            // Data is deliberately left alone (INV-5): the last good prices stay on screen and only
            // the display state retreats.
            return RoundOutcome.LeagueUnresolved;
        }

        _store.Set(AppConditionKind.LeagueUnresolved, false, null);

        // Step 5. The comparison is against this service's own record of the last settled league,
        // not against the store's DataLeague: the store applies commands asynchronously, so reading
        // it back here could see the previous world and start a second league transition, bumping
        // the epoch again and throwing away data that had just been committed.
        var leagueChanged = !string.Equals(league, _lastResolvedLeague, StringComparison.Ordinal);
        if (leagueChanged)
        {
            Interlocked.Increment(ref _dataEpoch);
            Interlocked.Increment(ref _roundGeneration);
            _lastResolvedLeague = league;
            _store.BeginNewLeague(league, DataEpoch);
        }

        // Step 6.
        var ctx = new RoundContext(league, DataEpoch, RoundGeneration, roundNumber, _timeProvider.GetUtcNow());

        // A league transition empties the store, so the previous world's medians and cooldowns must
        // not be consulted for this round. Using the pre-transition snapshot would compare against
        // another league's prices and reject every first commit as a median jump.
        var baseline = leagueChanged ? NoCategories : before.Categories;
        var statuses = leagueChanged ? NoStatuses : before.CategoryStatuses;
        var previousRate = leagueChanged ? null : before.Rate;

        // Step 7.
        var categories = ResolveCategorySet(settings.Watchlist, statuses, ctx.StartedAt);

        // Step 8.
        var results = await FetchAllAsync(ctx.League, categories, ct).ConfigureAwait(false);

        // Steps 9 – 11.
        return CommitResults(ctx, settings, results, baseline, statuses, previousRate);
    }

    private async Task<LeagueList> FetchLeagueListAsync(CancellationToken ct)
    {
        var result = await _market.FetchLeaguesAsync(RequestPriority.Polling, ct).ConfigureAwait(false);
        return result switch
        {
            MarketResult<LeagueList>.Ok ok => ok.Value,

            // Fail is reserved for Market's boundary catch, which is an unexpected exception rather
            // than a verdict; it still has to become a verdict here so the round can continue.
            MarketResult<LeagueList>.Fail fail => new LeagueList(
                [], _timeProvider.GetUtcNow(), LeagueListStatus.Failed, fail.Why.Code),
            _ => throw new NotSupportedException($"Unhandled league result {result.GetType().Name}."),
        };
    }

    /// <summary>
    /// Settles the league for one round (S2 7.3 step 4).
    /// </summary>
    /// <remarks>
    /// The configured value is trimmed, and that is not cosmetic. D6 allows free text, so
    /// <c>"Allflame "</c> is reachable; untrimmed it would tag every commit with a string that never
    /// equals the store's baseline, and the store would reject every one of them while the heartbeat
    /// stayed healthy and the screen stayed frozen. Case is deliberately <em>not</em> normalised:
    /// poe.ninja league ids are case sensitive, and folding them would query a league that does not
    /// exist.
    /// </remarks>
    internal static (LeagueResolutionState State, string? League, string? ReasonCode) ResolveLeague(
        string? settingsLeague,
        LeagueList leagues)
    {
        ArgumentNullException.ThrowIfNull(leagues);

        var configured = settingsLeague?.Trim();
        if (!string.IsNullOrEmpty(configured))
        {
            // An explicit league is honoured whatever the list says — the list is only needed to
            // guess, and the user has stopped guessing.
            return (LeagueResolutionState.Resolved, configured, null);
        }

        if (leagues.Status == LeagueListStatus.Ok && leagues.Entries.Count > 0)
        {
            return (LeagueResolutionState.Resolved, leagues.Entries[0].Id, null);
        }

        // Suspicious lists carry no failure code of their own — Market's verdict table only fills
        // one in for Failed — so the reason has to be named here.
        var reason = leagues.Status == LeagueListStatus.Suspicious
            ? SuspiciousLeagueListReason
            : leagues.FailureCode ?? "LeagueListInvalid";

        return (LeagueResolutionState.Unresolved, null, reason);
    }

    /// <summary>
    /// The categories to request this round (S2 7.4).
    /// </summary>
    /// <remarks>
    /// Currency is always a candidate and, being enum value 1, always sorts first: securing the rate
    /// before the rest of the round means fewer categories have to fall back to an inherited rate.
    /// Currency is <em>not</em> exempt from cooldown — exempting it would hammer a failing endpoint
    /// every cycle, and a missing rate is a designed state, not an emergency.
    /// <para>
    /// The set is never returned empty, even when everything is cooling down. An empty round lands
    /// no commits at all, and the store reads a run of commit-free rounds as evidence that commits
    /// are being rejected — it would raise CommitRejected, with a stale detail, while the truth is
    /// that nothing was even attempted. Keeping the candidate whose cooldown expires soonest costs
    /// one request per interval, which is no more traffic than a healthy single-category round.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<ExchangeCategory> ResolveCategorySet(
        EquatableArray<WatchlistEntry> watchlist,
        IReadOnlyDictionary<ExchangeCategory, CategoryStatus> statuses,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(statuses);

        var candidates = new SortedSet<ExchangeCategory> { ExchangeCategory.Currency };
        foreach (var entry in watchlist)
        {
            // An unresolved category is a settings problem, not a fetch problem: it is not requested
            // and its row says so (S2 10.5).
            if (entry.Category.Known is { } known)
            {
                candidates.Add(known);
            }
        }

        var available = new List<ExchangeCategory>(candidates.Count);
        ExchangeCategory? soonest = null;
        DateTimeOffset? soonestUntil = null;

        foreach (var category in candidates)
        {
            var until = statuses.TryGetValue(category, out var status) ? status.CooldownUntil : null;
            if (until is null || until.Value <= now)
            {
                available.Add(category);
                continue;
            }

            if (soonestUntil is null || until.Value < soonestUntil.Value)
            {
                soonest = category;
                soonestUntil = until;
            }
        }

        if (available.Count == 0 && soonest is { } fallback)
        {
            available.Add(fallback);
        }

        return available;
    }

    /// <summary>S2 7.7 — <c>interval × min(2^(failures − 1), 8)</c>, with no permanent exclusion.</summary>
    internal static TimeSpan ComputeCooldown(int consecutiveFailures, int refreshIntervalMinutes)
    {
        // The exponent is clamped before the shift: a long-running outage drives the failure count
        // far past 32, and an unclamped shift wraps modulo 32 and would hand back a *shorter*
        // cooldown the longer the endpoint had been down.
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 10);
        var multiplier = Math.Min(1 << exponent, PollingOptions.MaxCooldownMultiplier);
        return TimeSpan.FromMinutes((double)refreshIntervalMinutes * multiplier);
    }

    /// <summary>
    /// D8-e (S2 7.5) — whether a median may be committed.
    /// </summary>
    /// <remarks>
    /// With no previous snapshot the check passes. Stating that explicitly is the point: the natural
    /// reading of "compare against the previous median" is that an absent one cannot be compared and
    /// must therefore be rejected, and an implementation that did so would never accept any first
    /// value and the app would hold no data at all, forever.
    /// </remarks>
    internal static bool IsMedianJumpAcceptable(decimal newMedian, decimal? previousMedian, int consecutiveMedianJumps)
        => !IsMedianJump(newMedian, previousMedian)
            || consecutiveMedianJumps >= PollingOptions.MedianJumpsBeforeForcedAccept;

    internal static bool IsMedianJump(decimal newMedian, decimal? previousMedian)
    {
        if (previousMedian is not { } previous || previous <= 0m || newMedian <= 0m)
        {
            return false;
        }

        var high = Math.Max(newMedian, previous);
        var low = Math.Min(newMedian, previous);
        return high / low > PollingOptions.MedianJumpRatio;
    }

    /// <summary>
    /// Extracts this round's divine rate, or inherits the previous one (S2 7.6).
    /// </summary>
    /// <remarks>
    /// Inheritance rewrites <c>Inherited</c> and nothing else. Refreshing <c>AcquiredAt</c> would
    /// postpone expiry forever — every inheritance would reset the clock the expiry test reads — and
    /// D16's age display would report a rate as fresh for as long as the Currency endpoint stayed
    /// down. An already-inherited rate is returned unchanged rather than copied, so the record is
    /// not needlessly reissued.
    /// </remarks>
    internal DivineRate? InheritOrExtractRate(
        MarketResult<CategorySnapshot>? currencyResult,
        DivineRate? previous,
        RoundContext ctx,
        TimeSpan rateMaxAge)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (currencyResult is MarketResult<CategorySnapshot>.Ok ok
            && ok.Value.Items.TryGetValue(DivineId, out var divine)
            && divine.PrimaryValue > 0m)
        {
            // FetchedAt, not now: using the clock here would give two values taken from the same
            // response two different acquisition instants, and Pricing's age min() would disagree
            // with itself.
            return new DivineRate(divine.PrimaryValue, ok.Value.FetchedAt, ctx.League, false);
        }

        if (previous is null || !string.Equals(previous.League, ctx.League, StringComparison.Ordinal))
        {
            return null;
        }

        var age = _timeProvider.GetUtcNow() - previous.AcquiredAt;
        if (age > rateMaxAge)
        {
            return null;
        }

        return previous.Inherited ? previous : previous with { Inherited = true };
    }

    private void SetPeriod(int refreshIntervalMinutes)
    {
        var timer = _timer;
        if (timer is null)
        {
            return;
        }

        try
        {
            // Measured twice: assigning Period restarts the wait in flight from the moment of the
            // assignment rather than applying retroactively to the tick already being awaited. A
            // 3 s period changed to 200 ms about 1.0 s in fired at 1216 ms — roughly 204 ms after
            // the change, not 200 ms after the tick began — and a 5 s period changed to 30 s at
            // t ≈ 3.01 s fired at t = 33.02 s rather than at t = 5 s or t = 30 s. So the elapsed
            // part of the current wait is discarded and the new interval is served in full. That is
            // a real, user-visible effect of changing the interval, recorded rather than worked
            // around.
            timer.Period = Interval(refreshIntervalMinutes);
        }
        catch (ObjectDisposedException)
        {
            // Shutdown raced the round's last step.
        }
    }
}
