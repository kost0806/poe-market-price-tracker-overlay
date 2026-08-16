namespace PoeOverlay.Core.Presentation.Fanout;

/// <summary>
/// The one periodic tick the application owns (S2 10.8 / HLD 3.3 / S4 11.1).
/// </summary>
/// <remarks>
/// Time alone changes the derived conditions (a heartbeat goes stale, a rate expires), and the
/// snapshot stops changing in exactly the situation those conditions exist to report. Without this
/// tick the watchdog loses its trigger precisely when it is needed (S3 9.1 D-PS3).
/// <para>
/// Owned by Presentation, driven by the Shell's <c>DispatcherTimer</c>, and driven by hand in
/// tests — otherwise D20's only watchdog would sit where no test can reach it.
/// </para>
/// </remarks>
public interface IUiTicker
{
    /// <summary>Raised on the UI thread once per period.</summary>
    event EventHandler? Tick;

    /// <summary>Starts ticking. The Shell calls this with 30 s just before <c>app.Run()</c> (S3 3.2 B4).</summary>
    void Start(TimeSpan period);

    /// <summary>Stops ticking. Idempotent.</summary>
    void Stop();
}
