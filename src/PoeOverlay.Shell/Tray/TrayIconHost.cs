using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using PoeOverlay.Composition;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Domain.Ports;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Presentation.ViewModels;

namespace PoeOverlay.Tray;

/// <summary>
/// The tray icon, and the only entry point the application has (FR-08-1 / S3 6 / S4 12.4).
/// </summary>
/// <remarks>
/// <para>
/// A pure WPF <c>Application</c> with no WinForms message loop drives <c>NotifyIcon</c> correctly
/// as long as <c>UseWindowsForms</c> is set, and the click handlers already run on the WPF UI
/// thread — <c>Dispatcher.CheckAccess()</c> was true — so there is no marshalling here
/// (<c>00-shell-measurements.md</c> §4, D1 and D2).
/// </para>
/// <para>
/// There is deliberately no <c>TaskbarCreated</c> handling. <c>NotifyIcon</c> recovers by itself:
/// after deleting the icon behind its back the synthetic broadcast restored it, <c>hr=0x0</c>
/// (measured §4, D3). Re-registering by hand would only add a second <c>NIM_ADD</c> path.
/// </para>
/// <para>
/// Nor is <c>Shell_NotifyIconGetRect</c> used anywhere: it returned <c>S_OK</c> together with the
/// chevron's rectangle rather than the icon's, so its HRESULT proves nothing (measured §4.1).
/// </para>
/// </remarks>
internal sealed class TrayIconHost : IDisposable
{
    private readonly TrayViewModel _viewModel;
    private readonly IConditionSink _conditionSink;
    private readonly SettingsWindowFactory _settingsWindowFactory;
    private readonly IOverlayModeService _moveMode;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _timeProvider;
    private readonly Action _requestShutdown;
    private readonly Func<string> _logFolderPath;
    private readonly ILogger<TrayIconHost> _logger;

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _openSettingsItem;
    private readonly ToolStripMenuItem _moveModeOffItem;
    private readonly ToolStripMenuItem _exitItem;

    private int _showFailures;
    private int _disposed;

    /// <summary>Builds the tray icon, unregistered.</summary>
    /// <param name="viewModel">Owns the icon variant and the tooltip text (D21).</param>
    /// <param name="conditionSink">Where <c>TrayUnavailable</c> is raised and cleared.</param>
    /// <param name="settingsWindowFactory">The click destination.</param>
    /// <param name="moveMode">Backs the "turn off move mode" item.</param>
    /// <param name="localizer">Menu strings; every one is looked up by id (FR-07-4).</param>
    /// <param name="timeProvider">Clock for the asynchronous re-registration backoff.</param>
    /// <param name="requestShutdown">The single caller of <c>Application.Shutdown()</c> (HLD 3.6).</param>
    /// <param name="logFolderPath">Used by the escalation message box.</param>
    /// <param name="logger">Diagnostics.</param>
    internal TrayIconHost(
        TrayViewModel viewModel,
        IConditionSink conditionSink,
        SettingsWindowFactory settingsWindowFactory,
        IOverlayModeService moveMode,
        ILocalizer localizer,
        TimeProvider timeProvider,
        Action requestShutdown,
        Func<string> logFolderPath,
        ILogger<TrayIconHost> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(conditionSink);
        ArgumentNullException.ThrowIfNull(settingsWindowFactory);
        ArgumentNullException.ThrowIfNull(moveMode);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(requestShutdown);
        ArgumentNullException.ThrowIfNull(logFolderPath);
        ArgumentNullException.ThrowIfNull(logger);

        _viewModel = viewModel;
        _conditionSink = conditionSink;
        _settingsWindowFactory = settingsWindowFactory;
        _moveMode = moveMode;
        _localizer = localizer;
        _timeProvider = timeProvider;
        _requestShutdown = requestShutdown;
        _logFolderPath = logFolderPath;
        _logger = logger;

        _openSettingsItem = new ToolStripMenuItem(_localizer.Ui("ui.tray.openSettings"));
        _openSettingsItem.Click += (_, _) => ShowSettings();

        _moveModeOffItem = new ToolStripMenuItem(_localizer.Ui("ui.tray.movePositionOff"))
        {
            Visible = _viewModel.ShowMoveModeOffMenuItem,
        };
        _moveModeOffItem.Click += (_, _) => _moveMode.ExitMoveMode(MoveModeExitReason.TrayMenu);

        _exitItem = new ToolStripMenuItem(_localizer.Ui("ui.tray.exit"));
        _exitItem.Click += (_, _) => _requestShutdown();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_openSettingsItem, _moveModeOffItem, new ToolStripSeparator(), _exitItem]);

        _icon = new NotifyIcon
        {
            Icon = IconFor(_viewModel.IconVariant),
            Text = Truncate(_viewModel.TooltipText),
            ContextMenuStrip = menu,
            Visible = false,
        };
        _icon.MouseUp += OnMouseUp;
        _icon.DoubleClick += (_, _) => ShowSettings();

        _viewModel.PropertyChanged += OnViewModelChanged;
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>True once <c>NIM_ADD</c> has succeeded.</summary>
    internal bool IsRegistered => _icon.Visible;

    /// <summary>
    /// Registers the icon, with a short synchronous backoff (S3 6.2 D-SH5).
    /// </summary>
    /// <returns>True when the icon is visible.</returns>
    /// <remarks>
    /// Synchronous on purpose. This runs between <c>new Application()</c> and <c>app.Run()</c>,
    /// where there is no synchronisation context at all — <c>SynchronizationContext.Current</c> is
    /// null both before and after the <c>Application</c> is constructed, so an <c>await</c> here
    /// resumes on a pool thread and the next line touching a <c>DispatcherObject</c> throws
    /// (S3 1.4 R1). Blocking is harmless because there is no pump yet to block.
    /// </remarks>
    internal bool TryRegister()
    {
        for (var attempt = 1; attempt <= ShellConstants.TrayRegisterAttempts; attempt++)
        {
            if (TrySetVisible())
            {
                _conditionSink.Set(AppConditionKind.TrayUnavailable, false, null);
                return true;
            }

            if (attempt < ShellConstants.TrayRegisterAttempts)
            {
                Thread.Sleep(ShellConstants.TrayRegisterRetrySpacing);
            }
        }

        _logger.LogError("Tray icon registration failed after {Attempts} attempts.", ShellConstants.TrayRegisterAttempts);
        _conditionSink.Set(AppConditionKind.TrayUnavailable, true, "NIM_ADD failed");
        return false;
    }

    /// <summary>
    /// User-initiated re-registration, for use once the pump is running (S3 6.2 D-SH12).
    /// </summary>
    /// <param name="ct">Cancels the backoff.</param>
    /// <returns>True when the icon came back.</returns>
    /// <remarks>
    /// Not the synchronous backoff above: that one was justified by there being no pump to freeze,
    /// and this path runs from a button click in the middle of one.
    /// </remarks>
    internal async Task<bool> TryReregisterAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= ShellConstants.TrayReregisterAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (TrySetVisible())
            {
                _conditionSink.Set(AppConditionKind.TrayUnavailable, false, null);
                return true;
            }

            if (attempt < ShellConstants.TrayReregisterAttempts)
            {
                await Task.Delay(ShellConstants.TrayReregisterRetrySpacing, _timeProvider, ct);
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Idempotent: normal shutdown and both fatal-exception handlers all route through the same
        // teardown method, and may reach it more than once (S4 12.1 B6).
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelChanged;
        _localizer.LanguageChanged -= OnLanguageChanged;
        _icon.MouseUp -= OnMouseUp;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private static Icon IconFor(TrayIconVariant variant) => variant switch
    {
        TrayIconVariant.Warning => SystemIcons.Warning,
        TrayIconVariant.Error => SystemIcons.Error,
        _ => SystemIcons.Application,
    };

    private static string Truncate(string text)
        => text.Length <= 63 ? text : text[..63];

    private bool TrySetVisible()
    {
#pragma warning disable CA1031 // NIM_ADD failure surfaces as TrayUnavailable, not as a crash (S3 6.2).
        try
        {
            _icon.Visible = true;
            return _icon.Visible;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NIM_ADD attempt failed.");
            return false;
        }
#pragma warning restore CA1031
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        // Already on the WPF UI thread (measured §4, D2).
        if (e.Button == MouseButtons.Left)
        {
            ShowSettings();
        }
    }

    private void ShowSettings()
    {
#pragma warning disable CA1031 // D12: this path is the entry point, so it carries its own catch.
        try
        {
            _settingsWindowFactory.ShowAndActivate();
            _showFailures = 0;
        }
        catch (Exception ex)
        {
            _showFailures++;
            _logger.LogError(ex, "Tray click could not show the settings window ({Count} in a row).", _showFailures);

            if (_showFailures >= ShellConstants.TrayShowFailureEscalation)
            {
                // When neither WPF nor the tray can be trusted, a native message box is what is
                // left (S3 10.1).
                _ = MessageBox.Show(
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        NativeDialogText.SettingsWindowUnavailable,
                        _logFolderPath()),
                    "PoE Market Price Tracker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                _showFailures = 0;
            }
        }
#pragma warning restore CA1031
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TrayViewModel.IconVariant):
                _icon.Icon = IconFor(_viewModel.IconVariant);
                break;
            case nameof(TrayViewModel.TooltipText):
                _icon.Text = Truncate(_viewModel.TooltipText);
                break;
            case nameof(TrayViewModel.ShowMoveModeOffMenuItem):
                _moveModeOffItem.Visible = _viewModel.ShowMoveModeOffMenuItem;
                break;
            default:
                break;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _openSettingsItem.Text = _localizer.Ui("ui.tray.openSettings");
        _moveModeOffItem.Text = _localizer.Ui("ui.tray.movePositionOff");
        _exitItem.Text = _localizer.Ui("ui.tray.exit");
    }
}
