namespace PoeOverlay.Core.Polling;

/// <summary>
/// Turns gateway counter samples into a single "the gateway has stopped issuing" verdict.
/// </summary>
/// <remarks>
/// <c>NinjaGateway.ActiveCount</c> and <c>QueuedCount</c> had no production reader: if a slot ever
/// leaked — every one of the four measured pitfalls in that class is a slot-leak shape — each
/// category would time out forever and the symptom would be indistinguishable from poe.ninja being
/// down. No condition, log line or heartbeat field could tell the two apart. This is the consumer
/// the gateway's XML notes name.
/// <para>
/// The verdict is a pure function of samples so it can be tested without a scheduler: a queue that
/// is non-empty while nothing is in flight means nothing is left to release the slots, a state no
/// healthy schedule reaches for longer than the 250 ms issue floor.
/// </para>
/// </remarks>
internal sealed class GatewayStallMonitor(TimeSpan threshold)
{
    private DateTimeOffset? _stalledSince;
    private bool _reported;

    /// <summary>The instant the current stall began, or null when the gateway is healthy.</summary>
    public DateTimeOffset? StalledSince => _stalledSince;

    /// <summary>
    /// Records one sample.
    /// </summary>
    /// <returns>
    /// True exactly once per stall episode: on the first sample at or past the threshold. A stall
    /// that clears and returns is a new episode and reports again; a stall that simply continues
    /// does not, so a wedged gateway does not fill the log with the same line.
    /// </returns>
    public bool Observe(int activeCount, int queuedCount, DateTimeOffset now)
    {
        if (queuedCount <= 0 || activeCount != 0)
        {
            _stalledSince = null;
            _reported = false;
            return false;
        }

        _stalledSince ??= now;

        if (_reported || now - _stalledSince.Value < threshold)
        {
            return false;
        }

        _reported = true;
        return true;
    }
}
