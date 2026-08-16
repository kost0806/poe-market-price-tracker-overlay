namespace PoeOverlay.Core.Polling;

/// <summary>
/// What the trigger channel carries (S2 7.2 D-PL2 / S4 9.1).
/// </summary>
/// <remarks>
/// Deliberately narrower than <c>Domain.RoundTrigger</c>. The transport needs only two values;
/// the loop widens them to the four-member domain concept when it dequeues (S4 9.3), which is why
/// there is no third channel for league changes.
/// <para>
/// One channel, not <c>Task.WhenAny</c> over a timer task and a semaphore. Measured: when the tick
/// won, the abandoned <c>WaitAsync</c> stayed queued and took the next <c>Release()</c>, so the
/// live waiter saw nothing and every round that the tick won silently swallowed one repoll — the
/// user edited the watchlist and nothing happened, with a healthy heartbeat throughout. A channel
/// buffers the trigger when no one is reading, so the loss axis does not exist.
/// </para>
/// </remarks>
public enum PollingTriggerKind
{
    /// <summary>The periodic timer fired.</summary>
    Scheduled,

    /// <summary>A settings change asked for an extra round.</summary>
    Repoll,
}
