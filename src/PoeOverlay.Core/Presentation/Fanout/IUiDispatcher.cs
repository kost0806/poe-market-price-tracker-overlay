namespace PoeOverlay.Core.Presentation.Fanout;

/// <summary>
/// The UI thread, as much of it as <c>net8.0</c> may know (S3 7.2 D-PS1 / S4 11.1).
/// </summary>
/// <remarks>
/// Implemented in the Shell over <c>Dispatcher.CurrentDispatcher</c>; tests substitute a
/// synchronous stub, which HLD 3.4 already assumed existed when it warned that such a stub turns
/// raise → handler → commit into a recursion.
/// </remarks>
public interface IUiDispatcher
{
    /// <summary>True when the calling thread is the UI thread.</summary>
    bool CheckAccess();

    /// <summary>
    /// True once the dispatcher has begun shutting down, after which <see cref="Post"/> is a no-op.
    /// </summary>
    /// <remarks>
    /// <see cref="SnapshotFanout"/> reads this <em>before</em> claiming its merge flag (S3 8.2 M4):
    /// claiming the flag and then failing to post would leave the fan-out permanently deaf.
    /// </remarks>
    bool HasShutdownStarted { get; }

    /// <summary>Queues <paramref name="action"/> onto the UI thread. Never runs it inline in production.</summary>
    void Post(Action action, UiPostPriority priority = UiPostPriority.Normal);
}
