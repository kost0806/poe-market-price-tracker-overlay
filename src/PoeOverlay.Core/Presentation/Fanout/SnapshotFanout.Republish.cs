using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Domain;

namespace PoeOverlay.Core.Presentation.Fanout;

/// <summary>The signal path of S3 8.1: trigger → merge → post → pass → deferred flush.</summary>
public sealed partial class SnapshotFanout
{
    private void OnSnapshotChanged(object? sender, EventArgs e) => Schedule();

    private void OnTick(object? sender, EventArgs e) => Schedule();

    /// <summary>
    /// The merge (S3 8.2 D-PS2).
    /// </summary>
    /// <remarks>
    /// Order is load-bearing three times over. <see cref="IUiDispatcher.HasShutdownStarted"/> is
    /// read before the compare-and-swap so that a shutdown race cannot leave the flag raised with
    /// no pass to lower it. The swap itself is interlocked because the setter runs on the Store's
    /// consumer thread and the resetter on the UI thread. And whatever happens, a claim that did
    /// not result in a queued post is released in the <c>finally</c> — the alternative is silence
    /// for the rest of the session.
    /// </remarks>
    private void Schedule()
    {
        if (_disposed || _uiDispatcher.HasShutdownStarted)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _postPending, 1, 0) != 0)
        {
            return;
        }

        var posted = false;
        try
        {
            if (!_uiDispatcher.HasShutdownStarted)
            {
                _uiDispatcher.Post(Republish, UiPostPriority.Normal);
                posted = true;
            }
        }
#pragma warning disable CA1031 // A throwing dispatcher must not propagate into the Store's consumer loop (HLD 3.4).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log(LogLevel.Error, "FanoutPostFailed", "the UI post for a snapshot republish failed", ex);
        }
        finally
        {
            if (!posted)
            {
                Volatile.Write(ref _postPending, 0);
            }
        }
    }

    /// <summary>
    /// One UI pass: one <c>now</c>, one snapshot, every subscriber, then the deferred diagnostics.
    /// </summary>
    /// <remarks>
    /// The flag is reset before <c>store.Current</c> is read, which is what makes the merge lossless
    /// under pressure: a snapshot published between the reset and the read raises the flag again and
    /// books the next pass, and this pass still sees a value at least as new as the one that woke it.
    /// </remarks>
    private void Republish()
    {
        Interlocked.Exchange(ref _postPending, 0);

        var now = _timeProvider.GetUtcNow();
        var snapshot = _snapshotSource.Current;

        Subscription[] pass;
        lock (_sync)
        {
            pass = _subscribers.ToArray();
        }

        var deferred = new List<Action>();

        UiPassGuard.Enter();
        try
        {
            foreach (var subscription in pass)
            {
                RefreshOne(subscription, snapshot, now, deferred);
            }
        }
        finally
        {
            UiPassGuard.Exit();
            PassCount++;
        }

        RunDeferred(deferred);
    }

    private void RefreshOne(
        Subscription subscription,
        MarketSnapshot snapshot,
        DateTimeOffset now,
        List<Action> deferred)
    {
        try
        {
            subscription.Target.Refresh(snapshot, now);
        }
#pragma warning disable CA1031 // S3 8.3: one view model's bug must not stop the other two.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Immediate, because Diagnostics is a log channel and does not go through the Store —
            // it is not what the re-entrancy argument of S3 8.4 is about.
            Log(
                LogLevel.Error,
                "ViewModelRefreshFailed",
                $"{subscription.Target.GetType().Name}.Refresh threw; it will be retried next pass",
                ex);

            OnRefreshFailed(subscription, ex, deferred);
            return;
        }

        OnRefreshSucceeded(subscription, deferred);
    }

    /// <summary>
    /// The edge trigger and its latch (S3 10.1 D-PS10, C3).
    /// </summary>
    /// <remarks>
    /// The condition is queued on the pass where the counter <em>first reaches</em> the threshold,
    /// and the latch keeps it from being queued again while failures continue. Measured: written
    /// this way, two failing subscribers settle after seven passes and stay there. Written as a
    /// level test — <c>count &gt;= N</c>, set unconditionally — the same two subscribers sustain
    /// roughly 128 600 republishes and 257 100 Store commands per second with no external input,
    /// because each <c>Set</c> publishes a snapshot that schedules the next pass.
    /// </remarks>
    private void OnRefreshFailed(
        Subscription subscription,
        Exception exception,
        List<Action> deferred)
    {
        subscription.ConsecutiveFailures++;

        if (subscription.Reported || subscription.ConsecutiveFailures != RefreshFailureThreshold)
        {
            return;
        }

        subscription.Reported = true;

        var detail = $"{subscription.Target.GetType().Name}: {exception.GetType().Name}";

        // Queued, never called here: the sink is the Store, and a command there publishes a
        // snapshot and raises SnapshotChanged (S3 8.4 P1).
        //
        // Exactly one Store command per crossing. S3 10.1's sentence names both sinks, but the
        // seven-pass convergence measured in S3 8.4 is only reproducible at one command per
        // failing subscriber, and a second command here would buy nothing: the condition is what
        // the tray tooltip and the settings banner render, and the exception itself is already in
        // the log from the immediate record above.
        deferred.Add(() => _conditionSink.Set(AppConditionKind.ViewModelRefreshFailing, true, detail));
    }

    /// <summary>
    /// The other edge (true → false), which is equally guarded.
    /// </summary>
    /// <remarks>
    /// The condition is one flag shared by every view model, so it is only cleared once no
    /// subscriber is still latched — otherwise the overlay recovering would silently clear a
    /// condition the tray is still failing under, and the banner would disappear while the fault
    /// continued.
    /// </remarks>
    private void OnRefreshSucceeded(Subscription subscription, List<Action> deferred)
    {
        if (subscription.ConsecutiveFailures == 0 && !subscription.Reported)
        {
            return;
        }

        subscription.ConsecutiveFailures = 0;

        if (!subscription.Reported)
        {
            return;
        }

        subscription.Reported = false;

        lock (_sync)
        {
            foreach (var other in _subscribers)
            {
                if (other.Reported)
                {
                    return;
                }
            }
        }

        deferred.Add(() => _conditionSink.Set(AppConditionKind.ViewModelRefreshFailing, false, null));
    }

    /// <summary>
    /// Runs the buffered diagnostics once, outside the guard, with the same per-item isolation the
    /// subscriber loop has (S3 8.1 M1).
    /// </summary>
    /// <remarks>
    /// Without the per-item catch, one throwing delegate would discard the rest of the buffer and —
    /// since the dispatcher allow-list is empty (D-SH13) — take the process with it. A build of this
    /// stage combined with a Store that has not yet learned <c>ViewModelRefreshFailing</c> is
    /// exactly such a case: the <c>Set</c> would throw synchronously on the storage-group check.
    /// </remarks>
    private void RunDeferred(IReadOnlyList<Action> deferred)
    {
        for (var i = 0; i < deferred.Count; i++)
        {
            try
            {
                deferred[i]();
            }
#pragma warning disable CA1031 // One failed diagnostic must not cancel the rest of the buffer.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log(LogLevel.Error, "FanoutDeferredFailed", "a deferred diagnostic call failed", ex);
                ReportDeferredFailure(ex);
            }
        }
    }

    /// <summary>
    /// The last channel left when a queued condition never lands.
    /// </summary>
    /// <remarks>
    /// If the <c>Set</c> throws, the banner and the tooltip will never mention the fault, and the
    /// log is the only remaining trace — which is exactly the channel that may itself be
    /// unavailable (<c>LoggingUnavailable</c>). Called directly rather than queued: the guard is
    /// already down, and this path only runs when a queued call has failed, so it cannot sustain
    /// itself.
    /// </remarks>
    private void ReportDeferredFailure(Exception cause)
    {
        try
        {
            _errorSink.Report(new ErrorRecord(
                _timeProvider.GetUtcNow(),
                "Shell",
                "FanoutDeferredFailed",
                "ui.error.generic",
                cause.Message,
                null,
                null,
                null,
                cause.GetType().Name));
        }
#pragma warning disable CA1031 // The last channel failing must not take the process with it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log(LogLevel.Error, "FanoutErrorSinkFailed", "the error sink refused a deferred failure", ex);
        }
    }

    private void Log(LogLevel level, string code, string message, Exception? exception = null)
        => _logger.Log(level, new EventId(0, code), message, exception, static (state, _) => state);
}
