using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Store;

/// <summary>
/// The consumer loop, commit validation and command application (S2 6.3 / 6.4, S4 8.5).
/// </summary>
public sealed partial class Store
{
    /// <summary>S2 6.4 D-ST4 — this many consecutive commit-free rounds raise the condition.</summary>
    internal const int EmptyCommitRoundsBeforeCondition = 2;

    /// <summary>
    /// The single consumer. Commands are applied strictly in order, one at a time.
    /// </summary>
    /// <param name="lifetimeToken">
    /// A hard-timeout handle only, deliberately never passed to <c>ReadAllAsync</c> — see
    /// <see cref="StopAsync"/>.
    /// </param>
    private async Task ConsumeAsync(CancellationToken lifetimeToken)
    {
        _ = lifetimeToken;

        // Faulted until the loop is seen to reach the end of a completed channel. The exit kind is
        // the whole difference between "the application is shutting down" and "the store can no
        // longer apply anything" — logging both at Error put an [ERR] line in every clean run and
        // made the one that matters indistinguishable from the one that does not.
        var exitKind = LoopExitKind.Faulted;

        try
        {
            // CA2007 does not see await foreach, so ConfigureAwait is written out by hand — and
            // this is one of the two places S2 1.4's argument is strongest.
            await foreach (var command in _channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
#pragma warning disable CA1031 // S2 9.5 row 2: Error entry plus a lastError update. The loop must not die.
                try
                {
                    Apply(command);
                }
                catch (Exception ex)
                {
                    // Survival alone is not enough: a lost command used to be completely invisible.
                    // RejectedCommitCount does not move (this is not a rejection), Version does not
                    // move (no snapshot was built), and the heartbeat that follows applies normally
                    // and reports Completed. So the same catch updates lastError.
                    Log(
                        LogLevel.Error,
                        "ApplyFault",
                        Invariant($"Applying {command.GetType().Name} threw."),
                        ex);

                    // This is the one channel designed to survive Apply throwing, so it carries
                    // everything the store can see: the failing command's category, if it has one,
                    // and the league the store is currently holding. RoundNumber stays null because
                    // D-ST1 keeps RoundContext out of commands — the store never learns it (S2
                    // 12-33). It is null by construction, not by omission.
                    Post(new StoreCommand.SetLastErrorCmd(new ErrorRecord(
                        _timeProvider.GetUtcNow(),
                        "Store",
                        "ApplyFault",
                        "ui.error.applyFault",
                        ex.Message,
                        CategoryOf(command),
                        Current.DataLeague,
                        null,
                        ex.GetType().Name)));
                }
#pragma warning restore CA1031
            }

            // Reached only by draining a channel that StopAsync completed — the ordinary exit.
            exitKind = LoopExitKind.Canceled;
        }
        finally
        {
            var faulted = exitKind == LoopExitKind.Faulted;
            Log(
                faulted ? LogLevel.Error : LogLevel.Information,
                "LoopExited",
                faulted
                    ? "The store consumer loop exited on a fault; no further command can be applied."
                    : "The store consumer loop exited after the command channel closed.");
        }
    }

    /// <summary>
    /// Applies one command, which always produces exactly one new snapshot.
    /// </summary>
    /// <remarks>
    /// No merge optimisation. Publishing costs a reference swap and one event, the fan-out already
    /// coalesces UI posts, and merging would make <c>Version</c> timing-dependent — which is the
    /// one thing that field exists to prevent.
    /// </remarks>
    private void Apply(StoreCommand command)
    {
        var current = _current;
        var now = _timeProvider.GetUtcNow();

        if (!Validate(command, current, out var rejectCode))
        {
            _lastRejectCode = rejectCode;

            // Rejections are counted, published and logged — never silently dropped. The data slots
            // keep their references; only the counter and the version move. lastError is untouched:
            // a transient rejection is not an error to put in front of the user.
            Log(
                LogLevel.Warning,
                rejectCode ?? RejectionCodes.DefaultTag,
                Invariant($"Rejected {command.GetType().Name}."));

            Publish(current with { RejectedCommitCount = current.RejectedCommitCount + 1 });
            return;
        }

        switch (command)
        {
            case StoreCommand.BeginNewLeague c:
                // INV-8 moves the whole data world in one command so no partial state survives, and
                // the empty-round streak is state derived from the old world: carrying it forward
                // lets one stale empty round plus one slow first round — an ordinary cold start —
                // raise the banner against healthy new data, while carrying the raised condition
                // forward would leave the accusation standing with nothing left to accuse. The last
                // reject code goes with them: it names a league that no longer exists, and it is the
                // Detail the next raised condition would quote.
                _lastRejectCode = null;
                Publish(current with
                {
                    Categories = NoCategories,
                    CategoryStatuses = NoStatuses,
                    Rate = null,
                    Listing = null,
                    DataLeague = c.League,
                    DataEpoch = c.NewDataEpoch,
                    LeagueResolution = new LeagueResolution(LeagueResolutionState.Resolved, c.League, null),
                    ConsecutiveEmptyCommitRounds = 0,
                    Conditions = current.Conditions.ContainsKey(AppConditionKind.CommitRejected)
                        ? WithCondition(current.Conditions, AppConditionKind.CommitRejected, false, null, now)
                        : current.Conditions,
                });
                break;

            case StoreCommand.CommitCategory c:
                _landedCommitsThisRound++;
                Publish(current with
                {
                    Categories = With(current.Categories, c.Snapshot.Category, c.Snapshot),
                    CategoryStatuses = With(
                        current.CategoryStatuses,
                        c.Snapshot.Category,
                        SucceedStatus(StatusFor(current, c.Snapshot.Category), c.Snapshot, now)),
                });
                break;

            case StoreCommand.RecordCategoryFailure c:
                _landedCommitsThisRound++;

                // Categories is untouched by construction: a failure cannot reach the data slot.
                Publish(current with
                {
                    CategoryStatuses = With(
                        current.CategoryStatuses,
                        c.Category,
                        FailStatus(StatusFor(current, c.Category), c.Failure, now, c.CooldownUntil)),
                });
                break;

            case StoreCommand.CommitRate c:
                Publish(current with { Rate = c.Rate });
                break;

            case StoreCommand.SetFetchedListing c:
                Publish(current with
                {
                    Listing = new FetchedListing(
                        With(current.Listing?.ByCategory ?? NoCategories, c.Category, c.Snapshot),
                        c.Tag.League,
                        c.Tag.DataEpoch),
                });
                break;

            case StoreCommand.SetLeagueList c:
                Publish(current with { Leagues = c.List });
                break;

            case StoreCommand.SetLeagueUnresolved c:
                // Only the display state retreats. Categories, rate and listing stay (INV-5) —
                // the first edition's invariant could only be honoured by throwing data away,
                // which is a direct violation of FR-03-3.
                Publish(current with
                {
                    LeagueResolution = new LeagueResolution(LeagueResolutionState.Unresolved, null, c.ReasonCode),
                });
                break;

            case StoreCommand.RecordHeartbeatAttempt c:
                Publish(current with
                {
                    Heartbeat = current.Heartbeat with { LastRoundAttemptAt = now, LastRoundNumber = c.RoundNumber },
                });
                break;

            case StoreCommand.RecordHeartbeatOutcome c:
                ApplyRoundOutcome(current, c.Outcome, now);
                break;

            case StoreCommand.RecordLoopExit c:
                Publish(current with
                {
                    Heartbeat = current.Heartbeat with { LoopExited = true, ExitKind = c.Kind, ExitedAt = now },
                });
                break;

            case StoreCommand.SetLastErrorCmd c:
                Publish(current with { LastError = c.Error });
                break;

            case StoreCommand.SetConditionCmd c:
                ApplyCondition(current, c, now);
                break;

            default:
                throw new NotSupportedException($"Unhandled store command {command.GetType().Name}.");
        }
    }

    /// <summary>
    /// The category a command names, for the fault record (S4 13.5). Null for the commands that
    /// belong to no single category — a round outcome, a league list, a condition.
    /// </summary>
    private static string? CategoryOf(StoreCommand command) => command switch
    {
        StoreCommand.CommitCategory c => c.Snapshot.Category.ToString(),
        StoreCommand.RecordCategoryFailure c => c.Category.ToString(),
        StoreCommand.SetFetchedListing c => c.Category.ToString(),
        _ => null,
    };

    /// <summary>
    /// Commit validation (S2 6.4). Commands without a <see cref="DataTag"/> are not validated, and
    /// the reasons for <em>not</em> validating them matter more than the reasons for validating.
    /// </summary>
    private static bool Validate(StoreCommand command, MarketSnapshot current, out string? rejectCode)
    {
        rejectCode = null;

        DataTag tag;
        switch (command)
        {
            case StoreCommand.CommitCategory c:
                tag = c.Tag;
                break;
            case StoreCommand.RecordCategoryFailure c:
                tag = c.Tag;
                break;
            case StoreCommand.CommitRate c:
                tag = c.Tag;
                break;
            case StoreCommand.SetFetchedListing c:
                tag = c.Tag;
                break;
            default:
                return true;
        }

        if (current.DataLeague is null)
        {
            rejectCode = RejectionCodes.NoBaseline;
            return false;
        }

        // This check runs first and lives in Release: default(DataTag) is (null, 0), and at start-up
        // the baseline is (null, 0) too, so both != comparisons below would pass it through at
        // exactly the point where the invariant carries load.
        if (string.IsNullOrWhiteSpace(tag.League))
        {
            rejectCode = RejectionCodes.DefaultTag;
            return false;
        }

        if (tag.DataEpoch != current.DataEpoch)
        {
            rejectCode = RejectionCodes.EpochMismatch;
            return false;
        }

        if (!string.Equals(tag.League, current.DataLeague, StringComparison.Ordinal))
        {
            rejectCode = RejectionCodes.LeagueMismatch;
            return false;
        }

        if (command is StoreCommand.CommitCategory commit)
        {
            // default(ItemId) works perfectly well as a dictionary key, so if the mapper lets one
            // through nothing else will ever catch it.
            foreach (var key in commit.Snapshot.Items.Keys)
            {
                if (key.IsEmpty)
                {
                    rejectCode = RejectionCodes.EmptyItemId;
                    return false;
                }
            }
        }

        return true;
    }

    private static FrozenDictionary<TKey, TValue> With<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> source,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        var copy = new Dictionary<TKey, TValue>(source.Count + 1);
        foreach (var pair in source)
        {
            copy[pair.Key] = pair.Value;
        }

        copy[key] = value;
        return copy.ToFrozenDictionary();
    }

    private static CategoryStatus StatusFor(MarketSnapshot current, ExchangeCategory category)
        => current.CategoryStatuses.TryGetValue(category, out var status)
            ? status
            : new CategoryStatus(category, 0, null, null, null, null, 0, null, true);

    private static CategoryStatus SucceedStatus(CategoryStatus previous, CategorySnapshot snapshot, DateTimeOffset now)
        => previous with
        {
            ConsecutiveFailures = 0,
            LastAttemptAt = now,
            LastSuccessAt = now,
            CooldownUntil = null,
            LastFailure = null,
            ConsecutiveMedianJumps = 0,
            LastForcedAcceptAt = snapshot.ValidationBypassed ? now : previous.LastForcedAcceptAt,
            NeverNonEmpty = false,
        };

    private static CategoryStatus FailStatus(
        CategoryStatus previous,
        FailureRecord failure,
        DateTimeOffset now,
        DateTimeOffset? cooldownUntil)
        => previous with
        {
            ConsecutiveFailures = previous.ConsecutiveFailures + 1,
            LastAttemptAt = now,
            CooldownUntil = cooldownUntil ?? previous.CooldownUntil,
            LastFailure = failure,
            ConsecutiveMedianJumps = failure.Kind == FailureKind.MedianJump
                ? previous.ConsecutiveMedianJumps + 1
                : previous.ConsecutiveMedianJumps,
        };

    private static FrozenDictionary<AppConditionKind, ConditionState> WithCondition(
        IReadOnlyDictionary<AppConditionKind, ConditionState> conditions,
        AppConditionKind kind,
        bool active,
        string? detail,
        DateTimeOffset now)
    {
        // Since marks the transition, so a banner can say how long the state has held.
        var since = conditions.TryGetValue(kind, out var existing) && existing.Active == active
            ? existing.Since
            : now;

        return With(conditions, kind, new ConditionState(active, since, detail));
    }

    private static bool IsStoredCondition(AppConditionKind kind)
        => kind is AppConditionKind.LeagueUnresolved
            or AppConditionKind.CommitRejected
            or AppConditionKind.SettingsWriteFailed
            or AppConditionKind.SettingsCorrupt
            or AppConditionKind.SettingsReadOnly
            or AppConditionKind.SettingsUnreadable
            or AppConditionKind.TrayUnavailable
            or AppConditionKind.LoggingUnavailable
            or AppConditionKind.ViewModelRefreshFailing;

    private void ApplyCondition(MarketSnapshot current, StoreCommand.SetConditionCmd command, DateTimeOffset now)
    {
        if (!IsStoredCondition(command.Kind))
        {
            // The six derived conditions are computed at display time and are rejected here, in
            // Release too. Storing one would give the UI two disagreeing sources for the same fact.
            Log(
                LogLevel.Warning,
                RejectionCodes.DerivedCondition,
                Invariant($"Rejected SetCondition({command.Kind}): that condition is derived, never stored."));

            Publish(current with { });
            return;
        }

        Publish(current with
        {
            Conditions = WithCondition(current.Conditions, command.Kind, command.Active, command.Detail, now),
        });
    }

    /// <summary>
    /// Closes a round: counts commit-free rounds and drives the CommitRejected condition (D-ST4).
    /// </summary>
    /// <remarks>
    /// Without this the screen freezes indefinitely with every indicator healthy — failures are
    /// validated too, so ConsecutiveFailures stays at zero; heartbeats are not validated, so
    /// PollingStopped never fires; the round reports Completed and the rows keep showing the price
    /// from the moment rejection began. <c>RejectedCommitCount</c> had no consumer at all.
    /// </remarks>
    private void ApplyRoundOutcome(MarketSnapshot current, RoundOutcome outcome, DateTimeOffset now)
    {
        var landed = _landedCommitsThisRound;
        _landedCommitsThisRound = 0;

        // A cancelled round lands nothing *because it was cancelled* — S2 7.8 is explicit that
        // cancellation is not rejection — so it is evidence of neither health nor fault and leaves
        // the streak exactly where it was. Counting it was the same conflation D-ST1 keeps
        // RoundGeneration out of the store to prevent, arriving through the back door: two ordinary
        // debounced edits (S2 7.7) would reach the threshold and raise CommitRejected with a null
        // Detail, because Validate never ran. Commits that landed before the cancellation are real
        // evidence that the round reached the store, so they still reset it.
        //
        // LeagueUnresolved is the same shape and was found by following the same argument once
        // Polling existed to produce it (S2 7.3 step 4): a round that ends before a league is
        // settled makes no request and issues no commit, so two of them in a row would raise
        // CommitRejected — with a stale Detail naming a code from some earlier round — on top of
        // the LeagueUnresolved condition that is already saying the true thing. The user would be
        // told their data is being rejected while the truth is that nobody has decided which
        // league to ask about.
        var emptyRounds = landed > 0
            ? 0
            : outcome is RoundOutcome.Canceled or RoundOutcome.LeagueUnresolved
                ? current.ConsecutiveEmptyCommitRounds
                : current.ConsecutiveEmptyCommitRounds + 1;

        var conditions = current.Conditions;
        if (emptyRounds >= EmptyCommitRoundsBeforeCondition)
        {
            conditions = WithCondition(conditions, AppConditionKind.CommitRejected, true, _lastRejectCode, now);
        }
        else if (emptyRounds == 0)
        {
            conditions = WithCondition(conditions, AppConditionKind.CommitRejected, false, null, now);
        }

        Publish(current with
        {
            Heartbeat = current.Heartbeat with { LastRoundCompletedAt = now, LastOutcome = outcome },
            ConsecutiveEmptyCommitRounds = emptyRounds,
            Conditions = conditions,
        });
    }

    /// <summary>
    /// Publishes a new snapshot: version + 1, a paired <c>Volatile.Write</c>, then the signal.
    /// </summary>
    private MarketSnapshot Publish(MarketSnapshot next)
    {
        var published = next with { Version = _current.Version + 1 };

        // Paired with the Volatile.Read in Current. One half alone lets a reader see a
        // partly-initialised object: it happens to work on x86-64 and breaks on ARM64.
        Volatile.Write(ref _current, published);

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
        return published;
    }
}
