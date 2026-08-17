namespace PoeOverlay.Composition;

/// <summary>
/// The Shell half of the S4 15 constant tables, in one place so a value has one home.
/// </summary>
internal static class ShellConstants
{
    /// <summary>S4 15.6 — the named mutex the single-instance guard holds.</summary>
    internal const string MutexName = "PoeOverlay.SingleInstance.Mutex";

    /// <summary>S4 15.6 — the <c>RegisterWindowMessage</c> name; identical in both processes.</summary>
    internal const string SignalMessageName = "PoeOverlay.SingleInstance.Signal.v1";

    /// <summary>S4 15.6 — the message-only window class, which is also how the sender finds it (A4).</summary>
    internal const string SignalWindowClassName = "PoeOverlay.MessageOnlyWindow";

    /// <summary>Window text of the message-only window. Not used for discovery.</summary>
    internal const string SignalWindowTitle = "PoeOverlay signal receiver";

    /// <summary>
    /// S4 15.6 — the acknowledgement sentinel.
    /// </summary>
    /// <remarks>
    /// The sentinel <em>is</em> the acknowledgement. <c>DestroyWindow</c> releases a pending
    /// cross-process <c>SendMessageTimeout</c> with return <c>0x1</c> and
    /// <c>GetLastError == 0</c> while the handler never ran — measured twice, at 1,978 ms and
    /// 1,467 ms against an 8,000 ms timeout (<c>00-shell-measurements.md</c> §10.1). Treating that
    /// success as an ack makes the second instance believe it was handled and do nothing at all.
    /// </remarks>
    internal const int AckSentinel = 0x3039;

    /// <summary>S4 15.6 — attempts to locate the receiver window.</summary>
    internal const int FindWindowAttempts = 3;

    /// <summary>S4 15.6 — spacing between <c>FindWindowEx</c> attempts.</summary>
    internal static TimeSpan FindWindowRetrySpacing => TimeSpan.FromMilliseconds(100);

    /// <summary>S4 15.6 — per-attempt <c>SendMessageTimeout</c> timeout.</summary>
    internal static TimeSpan SendAttemptTimeout => TimeSpan.FromMilliseconds(2000);

    /// <summary>
    /// S4 15.6 — send attempts.
    /// </summary>
    /// <remarks>
    /// <c>SendMessageTimeout</c> does not queue, so a signal that arrives between the receiver
    /// being created (step 8) and the pump starting (step 11) simply times out. Three attempts of
    /// two seconds cover that window, which a normal boot crosses in hundreds of milliseconds
    /// (S3 3.2 C2).
    /// </remarks>
    internal const int SendAttempts = 3;

    /// <summary>S4 15.7 — synchronous <c>NIM_ADD</c> attempts before the pump exists.</summary>
    internal const int TrayRegisterAttempts = 3;

    /// <summary>S4 15.7 — spacing of the synchronous tray backoff.</summary>
    internal static TimeSpan TrayRegisterRetrySpacing => TimeSpan.FromMilliseconds(500);

    /// <summary>S4 15.7 — user-initiated re-registration attempts, once the pump is running.</summary>
    internal const int TrayReregisterAttempts = 3;

    /// <summary>S4 15.7 — spacing of the asynchronous re-registration backoff.</summary>
    internal static TimeSpan TrayReregisterRetrySpacing => TimeSpan.FromMilliseconds(1000);

    /// <summary>S3 10.1 — consecutive tray→settings failures before escalating to a native message box.</summary>
    internal const int TrayShowFailureEscalation = 3;

    /// <summary>S4 15.7 — move-mode inactivity threshold.</summary>
    internal static TimeSpan MoveModeIdleThreshold => TimeSpan.FromMinutes(5);

    /// <summary>S4 15.2 — the one periodic UI tick (HLD D20).</summary>
    internal static TimeSpan UiTickPeriod => TimeSpan.FromSeconds(30);

    /// <summary>S4 15.10 — how long boot may take before the fatal message box is shown anyway.</summary>
    internal static TimeSpan BootWatchdogTimeout => TimeSpan.FromSeconds(15);

    /// <summary>HLD 3.5 12 — the hard timeout around <c>host.StopAsync</c>.</summary>
    internal static TimeSpan ShutdownTimeout => TimeSpan.FromSeconds(5);

    /// <summary>
    /// S4 15.1 — the colour key, as a COLORREF (<c>0x00BBGGRR</c>).
    /// </summary>
    /// <remarks>
    /// Magenta is symmetric under the byte swap, so this reads the same either way; it is still a
    /// COLORREF and not an RGB literal. Provisional pending the palette (S4 19.5): a content pixel
    /// that happens to equal the key becomes a hole (measured §8.6).
    /// </remarks>
    internal const uint ColorKeyRef = 0x00FF00FF;

    /// <summary>The window class of the raw layered overlay parent (S3 4.0 D-SH20).</summary>
    internal const string OverlayWindowClassName = "PoeOverlay.LayeredOverlayHost";

    /// <summary>Window text of the overlay parent. Nothing discovers the overlay by it.</summary>
    internal const string OverlayWindowTitle = "PoE Market Price Tracker";

    /// <summary>Window text of the hosted <c>HwndSource</c> child.</summary>
    internal const string OverlayContentWindowTitle = "PoE Market Price Tracker content";

    /// <summary>S4 15.1 — provisional footer height in DIPs, the unit of the minimum-visible-area rule.</summary>
    internal const double FooterHeight = 20d;

    /// <summary>
    /// S4 15.1 — the folder beside the exe holding the item icons and their manifest (FR-04-6).
    /// </summary>
    /// <remarks>
    /// Beside the exe rather than inside the assembly: 5.3 MB of art that is regenerated every
    /// league, and a user must be able to drop a newer set in (HLD D23). The opposite decision from
    /// the bundled typeface (D-SH22), for the opposite reason — a missing icon costs a picture,
    /// a missing font costs the reason the app exists.
    /// </remarks>
    internal const string IconFolderName = "Icons";

    /// <summary>S4 15.3 — the named <c>IHttpClientFactory</c> client Market resolves.</summary>
    /// <remarks>Duplicated from Core's <c>internal NinjaEndpoints.HttpClientName</c>, which the Shell cannot see.</remarks>
    internal const string HttpClientName = "poe.ninja";

    /// <summary>S4 15.3 — the identifiable fixed User-Agent.</summary>
    internal const string UserAgent = "PoeOverlayPriceTracker/1.0";

    /// <summary>Folder under <c>%APPDATA%</c> holding settings and logs.</summary>
    internal const string AppDataFolderName = "PoeOverlay";

    /// <summary>Subfolder of the app-data folder holding rolling logs.</summary>
    internal const string LogFolderName = "logs";
}
