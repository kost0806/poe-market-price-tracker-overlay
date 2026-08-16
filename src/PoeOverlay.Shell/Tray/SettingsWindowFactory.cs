using System.ComponentModel;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Settings;
using PoeOverlay.Overlay;
using PoeOverlay.Settings;

namespace PoeOverlay.Tray;

/// <summary>
/// Creates, reuses and tears down the settings window (S4 12.4 B2).
/// </summary>
/// <remarks>
/// The window and its view model are transient — D18-b's reasoning, that a window kept alive
/// refreshes invisible UI on every snapshot, applies to this window and only this window. The
/// overlay and tray view models are composition-root singletons because they are supposed to update
/// while unobserved (S3 3.1 B5).
/// </remarks>
internal sealed class SettingsWindowFactory
{
    private static readonly TimeSpan CloseFlushTimeout = TimeSpan.FromSeconds(2);

    private readonly OverlayWindow _overlay;
    private readonly SnapshotFanout _fanout;
    private readonly Func<CancellationToken, SettingsViewModel> _viewModelFactory;
    private readonly ISettingsSource _settings;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SettingsWindowFactory> _logger;

    private SettingsWindow? _window;
    private SettingsViewModel? _viewModel;
    private SettingsEditor? _editor;
    private CancellationTokenSource? _windowScope;

    /// <summary>Wires the factory.</summary>
    /// <param name="overlay">Becomes the window's <c>Owner</c>.</param>
    /// <param name="fanout">Attached on create, detached on close.</param>
    /// <param name="viewModelFactory">The transient registration, given the window-scope token.</param>
    /// <param name="settings">Flushed on close, and the editor's backing store.</param>
    /// <param name="localizer">Supplies the attribution line.</param>
    /// <param name="logger">Diagnostics.</param>
    internal SettingsWindowFactory(
        OverlayWindow overlay,
        SnapshotFanout fanout,
        Func<CancellationToken, SettingsViewModel> viewModelFactory,
        ISettingsSource settings,
        ILocalizer localizer,
        ILogger<SettingsWindowFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(fanout);
        ArgumentNullException.ThrowIfNull(viewModelFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _overlay = overlay;
        _fanout = fanout;
        _viewModelFactory = viewModelFactory;
        _settings = settings;
        _localizer = localizer;
        _logger = logger;
    }

    /// <summary>Returns the live window, creating one if needed.</summary>
    /// <returns>The settings window.</returns>
    internal SettingsWindow GetOrCreate()
    {
        if (_window is not null)
        {
            return _window;
        }

        _windowScope = new CancellationTokenSource();
        _viewModel = _viewModelFactory(_windowScope.Token);
        _editor = new SettingsEditor(_settings, _localizer);

        var window = new SettingsWindow(_viewModel, _editor, _localizer.Ui("ui.footer.attribution"), _overlay);
        window.Closing += OnClosing;
        _window = window;

        _fanout.Attach(_viewModel);
        return window;
    }

    /// <summary>
    /// Shows the window and brings it forward.
    /// </summary>
    /// <remarks>
    /// The success oracle here is <c>GetForegroundWindow()</c> and nothing else:
    /// <c>window.IsActive</c> read true in every single failed activation of the measurement
    /// (§1.4), as do <c>GetActiveWindow</c> and <c>GetFocus</c>, which are thread-local.
    /// </remarks>
    internal void ShowAndActivate()
    {
        var window = GetOrCreate();
        window.Show();

        if (window.WindowState == System.Windows.WindowState.Minimized)
        {
            window.WindowState = System.Windows.WindowState.Normal;
        }

        _ = window.Activate();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // S3 5.3, in order. Flushing before cancelling only matters for log readability, but the
        // order is fixed so that the log reads the same way every time.
        FlushPendingSettings();
        _windowScope?.Cancel();

        if (_viewModel is not null)
        {
            _fanout.Detach(_viewModel);
            _viewModel.Dispose();
        }

        _editor?.Detach();
        _windowScope?.Dispose();

        if (sender is SettingsWindow window)
        {
            window.Closing -= OnClosing;
        }

        _window = null;
        _viewModel = null;
        _editor = null;
        _windowScope = null;

        // Move mode is deliberately untouched: closing this window is not a reason to force it off
        // (HLD D4-b).
    }

    private void FlushPendingSettings()
    {
#pragma warning disable CA1031 // A failed flush must not stop the window closing; the store records it.
        try
        {
            // On a pool thread, so a synchronisation-context round trip cannot deadlock the close.
            var flush = Task.Run(() => _settings.FlushAsync(CancellationToken.None));
            if (!flush.Wait(CloseFlushTimeout))
            {
                _logger.LogWarning("Settings flush did not complete within {Timeout} on window close.", CloseFlushTimeout);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings flush failed on window close.");
        }
#pragma warning restore CA1031
    }
}
