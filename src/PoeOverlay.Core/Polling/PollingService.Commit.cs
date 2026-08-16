using System.Globalization;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Pricing;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Store;

namespace PoeOverlay.Core.Polling;

/// <summary>
/// Fetching, the two context checks, and the per-category commits (S2 7.5 – 7.8).
/// </summary>
public sealed partial class PollingService
{
    private async Task<IReadOnlyList<(ExchangeCategory Category, MarketResult<CategorySnapshot> Result)>> FetchAllAsync(
        string league,
        IReadOnlyList<ExchangeCategory> categories,
        CancellationToken ct)
    {
        using var samplerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sampler = SampleGatewayAsync(samplerCts.Token);

        try
        {
            var tasks = new List<Task<(ExchangeCategory, MarketResult<CategorySnapshot>)>>(categories.Count);
            foreach (var category in categories)
            {
                tasks.Add(FetchOneAsync(league, category, ct));
            }

            // Concurrency is the gateway's business, not this loop's: NFR-02 constrains total
            // traffic, so the ceiling has to be process-wide rather than per caller.
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            await samplerCts.CancelAsync().ConfigureAwait(false);
            await sampler.ConfigureAwait(false);
        }
    }

    private async Task<(ExchangeCategory, MarketResult<CategorySnapshot>)> FetchOneAsync(
        string league,
        ExchangeCategory category,
        CancellationToken ct)
    {
        var result = await _market
            .FetchCategoryAsync(league, category, RequestPriority.Polling, ct)
            .ConfigureAwait(false);

        return (category, result);
    }

    /// <summary>
    /// Watches the gateway's counters while this round's requests are in flight.
    /// </summary>
    /// <remarks>
    /// A non-empty queue with nothing in flight means no request is left to release a slot, so the
    /// queue can never drain on its own. Sampled here rather than reported by the gateway itself,
    /// because the gateway must not know that conditions exist.
    /// </remarks>
    private async Task SampleGatewayAsync(CancellationToken ct)
    {
        if (SampleGatewayLoad() is null)
        {
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PollingOptions.GatewaySampleInterval, _timeProvider, ct).ConfigureAwait(false);

                if (SampleGatewayLoad() is not { } load)
                {
                    continue;
                }

                var (active, queued) = load;
                if (!_stallMonitor.Observe(active, queued, _timeProvider.GetUtcNow()))
                {
                    continue;
                }

                var detail = string.Create(
                    CultureInfo.InvariantCulture,
                    $"queued={queued} active={active}");

                Log(LogLevel.Error, "GatewayStalled", $"The poe.ninja gateway has issued nothing while requests are queued ({detail}).");
                _store.Report(new ErrorRecord(
                    _timeProvider.GetUtcNow(),
                    "Polling",
                    "GatewayStalled",
                    "ui.error.generic",
                    detail,
                    null,
                    _lastResolvedLeague,
                    RoundNumber,
                    null));
            }
        }
        catch (OperationCanceledException)
        {
            // The round finished; this is the normal way the sampler ends.
        }
    }

    /// <summary>
    /// Where the gateway counters come from.
    /// </summary>
    /// <remarks>
    /// A seam, because the stalled state is by construction unreachable from outside the gateway —
    /// it only arises from a slot leak, which is the bug being watched for. Without a way to
    /// arrange the state, the reporting path would be shipped untested, which is how the counters
    /// came to have no consumer in the first place.
    /// </remarks>
    internal Func<(int Active, int Queued)>? GatewayLoadSampler { get; set; }

    private (int Active, int Queued)? SampleGatewayLoad()
    {
        if (GatewayLoadSampler is { } custom)
        {
            return custom();
        }

        return _gateway is { } gateway ? (gateway.ActiveCount, gateway.QueuedCount) : null;
    }

    private RoundOutcome CommitResults(
        RoundContext ctx,
        AppSettings settings,
        IReadOnlyList<(ExchangeCategory Category, MarketResult<CategorySnapshot> Result)> results,
        IReadOnlyDictionary<ExchangeCategory, CategorySnapshot> baseline,
        IReadOnlyDictionary<ExchangeCategory, CategoryStatus> statuses,
        DivineRate? previousRate)
    {
        var tag = new DataTag(ctx.League, ctx.DataEpoch);
        var committed = 0;
        var failed = 0;
        FailureRecord? lastFailure = null;
        ExchangeCategory? lastFailedCategory = null;
        MarketResult<CategorySnapshot>? currencyResult = null;

        foreach (var (category, result) in results)
        {
            if (!StillCurrent(ctx))
            {
                return RoundOutcome.Canceled;
            }

            var now = _timeProvider.GetUtcNow();
            var status = statuses.TryGetValue(category, out var existing) ? existing : null;
            FailureRecord? failure = null;
            CategorySnapshot? snapshot = null;
            var forcedAccept = false;

            switch (result)
            {
                case MarketResult<CategorySnapshot>.Fail fail:
                    failure = fail.Why;
                    break;

                case MarketResult<CategorySnapshot>.Ok ok when category == ExchangeCategory.Currency
                    && !ok.Value.Items.ContainsKey(DivineId):

                    // D8-c. The absence of the divine line impugns the whole response, so the
                    // Currency data is not committed either — half of a response that lost its
                    // anchor is not half-good data.
                    failure = new FailureRecord(
                        FailureKind.DivineLineMissing, "DivineLineMissing", now, null, null, null);
                    break;

                case MarketResult<CategorySnapshot>.Ok ok when !string.Equals(
                    ok.Value.League, ctx.League, StringComparison.Ordinal):

                    // INV-1 says every committed snapshot belongs to DataLeague, but the store
                    // validates the command's tag, not the snapshot inside it — so a snapshot
                    // carrying the wrong league would be accepted and the invariant would break
                    // silently. The guard turns that into an ordinary, visible category failure.
                    failure = new FailureRecord(
                        FailureKind.MappingFault,
                        "LeagueMismatch",
                        now,
                        null,
                        string.Create(CultureInfo.InvariantCulture, $"snapshot={ok.Value.League} round={ctx.League}"),
                        null);
                    break;

                case MarketResult<CategorySnapshot>.Ok ok:
                {
                    var previousMedian = baseline.TryGetValue(category, out var previous)
                        ? previous.MedianPrimaryValue
                        : (decimal?)null;
                    var jumps = status?.ConsecutiveMedianJumps ?? 0;

                    if (!IsMedianJumpAcceptable(ok.Value.MedianPrimaryValue, previousMedian, jumps))
                    {
                        failure = new FailureRecord(
                            FailureKind.MedianJump,
                            "MedianJump",
                            now,
                            null,
                            string.Create(CultureInfo.InvariantCulture, $"prev={previousMedian} new={ok.Value.MedianPrimaryValue}"),
                            null);
                        break;
                    }

                    forcedAccept = IsMedianJump(ok.Value.MedianPrimaryValue, previousMedian);
                    snapshot = ok.Value;
                    break;
                }

                default:
                    throw new NotSupportedException($"Unhandled market result {result.GetType().Name}.");
            }

            if (category == ExchangeCategory.Currency)
            {
                // Held for the rate step. A Currency response that failed D8-c is not a usable rate
                // source, so only a genuinely committed snapshot is passed on.
                currencyResult = snapshot is null ? null : result;
            }

            if (failure is not null)
            {
                RecordFailure(ctx, tag, category, failure, status, settings);
                lastFailure = failure;
                lastFailedCategory = category;
                failed++;
                continue;
            }

            // Market has no epoch parameter and no way to learn one, so every snapshot it produces
            // carries DataEpoch 0. Stamping the round's epoch here is what makes INV-2 hold; without
            // it the store would accept the commit (validation reads the tag, not the snapshot) and
            // the snapshot would sit in the map claiming to belong to epoch 0 forever.
            var stamped = snapshot! with
            {
                DataEpoch = ctx.DataEpoch,
                ValidationBypassed = forcedAccept,
            };

            _store.CommitCategory(tag, stamped);
            committed++;

            if (forcedAccept)
            {
                // Forced acceptance commits as an ordinary success: the failure badge clears, the
                // staleness marker lifts and the row actively signals recovery. If the cause was a
                // response-format change rather than a real spike, that bad value becomes the new
                // baseline and every later round passes D8-e. An event more dangerous than a
                // rejection must not be quieter than one.
                Log(
                    LogLevel.Warning,
                    "MedianJumpForcedAccept",
                    $"Accepted a median jump for {category} after {PollingOptions.MedianJumpsBeforeForcedAccept.ToString(CultureInfo.InvariantCulture)} consecutive rejections.");

                _store.Report(new ErrorRecord(
                    now, "Polling", "MedianJumpForcedAccept", "ui.error.medianJump",
                    category.ToString(), category.ToString(), ctx.League, ctx.RoundNumber, null));
            }
        }

        // Step 10. The rate never affects the commit verdict (D1): its absence is a designed state
        // with its own display, not a failure of the round.
        var rate = InheritOrExtractRate(
            currencyResult, previousRate, ctx, StalenessPolicy.RateMaxAge(settings.RefreshIntervalMinutes));
        _store.CommitRate(tag, rate);

        if (lastFailure is not null)
        {
            _store.Report(new ErrorRecord(
                lastFailure.At,
                "Polling",
                lastFailure.Code,
                MessageKeyFor(lastFailure.Kind),
                lastFailure.Detail,
                lastFailedCategory?.ToString(),
                ctx.League,
                ctx.RoundNumber,
                lastFailure.ExceptionType));
        }

        return failed == 0
            ? RoundOutcome.Completed
            : committed == 0
                ? RoundOutcome.AllFailed
                : RoundOutcome.PartiallyFailed;
    }

    /// <summary>
    /// Records one category failure together with the cooldown it earns (S2 7.7).
    /// </summary>
    /// <remarks>
    /// The cooldown is computed here because the formula needs <c>refreshIntervalMinutes</c> and the
    /// store may not depend on Settings (S2 1.2). Categories excluded by a cooldown are not counted
    /// as failures of the round — counting them would let a cooldown extend itself indefinitely —
    /// which follows structurally from their never entering the request set at all.
    /// </remarks>
    private void RecordFailure(
        RoundContext ctx,
        DataTag tag,
        ExchangeCategory category,
        FailureRecord failure,
        CategoryStatus? status,
        AppSettings settings)
    {
        var consecutive = (status?.ConsecutiveFailures ?? 0) + 1;
        var cooldownUntil = _timeProvider.GetUtcNow()
            + ComputeCooldown(consecutive, settings.RefreshIntervalMinutes);

        _store.RecordCategoryFailure(tag, category, failure, cooldownUntil);

        Log(
            LogLevel.Warning,
            failure.Code,
            $"Round {ctx.RoundNumber.ToString(CultureInfo.InvariantCulture)}: {category} failed ({failure.Kind}).");
    }

    /// <summary>
    /// Re-checks the cancellation axis immediately before a commit (S2 7.8).
    /// </summary>
    /// <remarks>
    /// Commits that landed before the cancellation stay: they carry the same epoch, so they are not
    /// contamination, and discarding them would let an ordinary watchlist edit destroy sound data.
    /// Cancellation is logged at Debug and does not raise the rejection counter — it is not a
    /// rejection.
    /// </remarks>
    private bool StillCurrent(RoundContext ctx)
    {
        if (RoundGeneration == ctx.RoundGeneration)
        {
            return true;
        }

        Log(
            LogLevel.Debug,
            "RoundSuperseded",
            $"Round {ctx.RoundNumber.ToString(CultureInfo.InvariantCulture)} was superseded before its remaining commits.");
        return false;
    }

    /// <summary>S4 13.6 — one <c>ui.error.*</c> key per failure kind, with a defensive fallback.</summary>
    internal static string MessageKeyFor(FailureKind kind) => kind switch
    {
        FailureKind.Network => "ui.error.network",
        FailureKind.Timeout => "ui.error.timeout",
        FailureKind.HttpStatus => "ui.error.httpStatus",
        FailureKind.RateLimited => "ui.error.rateLimited",
        FailureKind.Deserialization => "ui.error.deserialization",
        FailureKind.EmptyLines => "ui.error.emptyLines",
        FailureKind.NoPricedLines => "ui.error.noPricedLines",
        FailureKind.FieldMissingRatio => "ui.error.fieldMissingRatio",
        FailureKind.PrimaryCurrencyMismatch => "ui.error.primaryCurrencyMismatch",
        FailureKind.DivineLineMissing => "ui.error.divineLineMissing",
        FailureKind.MedianJump => "ui.error.medianJump",
        FailureKind.LeagueListInvalid => "ui.error.leagueListInvalid",
        FailureKind.MappingFault => "ui.error.mappingFault",
        _ => "ui.error.generic",
    };
}
