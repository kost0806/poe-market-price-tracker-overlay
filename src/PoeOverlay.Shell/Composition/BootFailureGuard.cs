using System.Globalization;
using System.Windows.Forms;
using PoeOverlay.Core.Diagnostics;

namespace PoeOverlay.Composition;

/// <summary>
/// The last channel, used when neither WPF nor the Store can be relied on (S3 3.1 D-SH19).
/// </summary>
internal static class BootFailureGuard
{
    /// <summary>Shows the fatal boot dialog. Always displayed, whatever the logger managed.</summary>
    /// <param name="state">What boot had learned so far, if anything.</param>
    /// <param name="exception">The failure, or null when the watchdog fired on a stall.</param>
    /// <param name="logDirectory">Where the log folder would be.</param>
    internal static void ShowFatalMessageBox(DiagnosticsStartupState? state, Exception? exception, string logDirectory)
    {
        var detail = exception?.Message ?? "Start-up did not finish in time.";

        if (state?.LoggerOpenFailed == true)
        {
            detail += " The log file could not be opened either.";
        }

        if (state?.SettingsFlushFailureTracePath is { } trace)
        {
            detail += $" A previous shutdown failed to save settings ({trace}).";
        }

        _ = MessageBox.Show(
            string.Format(CultureInfo.CurrentCulture, NativeDialogText.BootFailed, detail, logDirectory),
            "PoE Market Price Tracker",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
