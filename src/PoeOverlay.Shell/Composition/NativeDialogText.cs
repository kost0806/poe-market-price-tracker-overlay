namespace PoeOverlay.Composition;

/// <summary>
/// The three fixed-English native dialog strings (S4 18.2 D-DL20).
/// </summary>
/// <remarks>
/// Not localised, and not because the first release ships English only. The process showing these
/// has either failed before <c>Localization</c> loaded or is a second instance that exits
/// immediately without building a host — in both cases <c>ILocalizer</c> does not exist to ask.
/// </remarks>
internal static class NativeDialogText
{
    /// <summary>
    /// Shown when the running instance did not acknowledge the signal.
    /// </summary>
    /// <remarks>
    /// Deliberately does not assert unreachability. A receiver busy inside the handler for six
    /// seconds produces the same timeout as a dead one, and then raises its settings window a few
    /// seconds later — measured (S3 3.2 M6). The wording has to survive that sequence.
    /// </remarks>
    internal const string InstanceUnreachable =
        "PoE Market Price Tracker did not respond in time. If it's already running, it should appear shortly. "
        + "If the problem continues, check the log folder:\n{0}";

    /// <summary>Shown when boot fails or stalls before the Store can accept a condition.</summary>
    internal const string BootFailed =
        "PoE Market Price Tracker failed to start.\n{0}\nCheck the log folder if it exists:\n{1}";

    /// <summary>Shown after repeated failures on the tray→settings path (S3 10.1).</summary>
    internal const string SettingsWindowUnavailable =
        "PoE Market Price Tracker could not open its settings window. Check the log folder:\n{0}";
}
