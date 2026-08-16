using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Domain.Ports;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Settings;
using PoeOverlay.Interop;
using PoeOverlay.Overlay;
using PoeOverlay.Startup;
using PoeOverlay.Tray;
using Application = System.Windows.Application;

namespace PoeOverlay.Composition;

/// <summary>
/// The entry point, following HLD 3.5 step for step (S4 12.1).
/// </summary>
/// <remarks>
/// An explicit <c>[STAThread] Main</c> rather than <c>Application.OnStartup</c>: calling
/// <c>host.StartAsync</c> from inside a dispatcher context pulls polling onto the UI thread
/// (HLD 3.2). Shutdown and the fatal handlers live here for the same symmetry, so
/// <c>App.xaml.cs</c> is not needed at all.
/// </remarks>
internal static class Program
{
    private static Application? _application;
    private static TrayIconHost? _trayHost;
    private static ILogger? _logger;
    private static int _teardownStarted;

    /// <summary>Runs the application.</summary>
    /// <returns>0 on a clean run, 1 when boot failed, 2 when a second instance was acknowledged, 3 when it was not.</returns>
    [STAThread]
    private static int Main()
    {
        var args = Environment.GetCommandLineArgs()[1..];
        var paths = AppPaths.CreateDefault();

        // 1 — the log file, before anything that could fail.
        var diagnostics = BootDiagnostics.Open(paths);
        _logger = diagnostics.Logger;

        // 2 — single instance. A second launch is a signal, not a second poller (NFR-02).
        var guard = new SingleInstanceGuard(ShellConstants.MutexName);
        if (!guard.TryAcquire())
        {
            guard.Dispose();
            var code = HandOverToRunningInstance(paths);
            diagnostics.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return code;
        }

        try
        {
            return Run(args, paths, diagnostics, guard);
        }
        finally
        {
            guard.Dispose();
            diagnostics.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static int Run(string[] args, AppPaths paths, BootDiagnostics diagnostics, SingleInstanceGuard guard)
    {
        // 3
        System.Windows.Forms.Application.EnableVisualStyles();

        // This thread owns the dispatcher and becomes the WPF UI thread at step 7.
        var dispatcher = Dispatcher.CurrentDispatcher;

        IHost host;
        using var bootWatchdog = new BootWatchdog(
            TimeProvider.System,
            () => BootFailureGuard.ShowFatalMessageBox(diagnostics.State, null, paths.LogDirectory));

#pragma warning disable CA1031 // D-SH19: a boot failure before the Store exists has no other channel.
        try
        {
            // 4
            host = HostBuilderFactory.Build(args, paths, diagnostics, dispatcher, RequestShutdown);

            // 5 — StartingAsync loads the dictionaries, then the settings; StartAsync starts the
            // Store and then Polling. The order matters: settings validation requires `language` to
            // be one of the discovered dictionaries.
            bootWatchdog.Arm();
            host.Start();
            bootWatchdog.Disarm();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Start-up failed before the Store was available.");
            BootFailureGuard.ShowFatalMessageBox(diagnostics.State, ex, paths.LogDirectory);
            return 1;
        }
#pragma warning restore CA1031

        var services = host.Services;
        var settings = services.GetRequiredService<ISettingsSource>();
        var localizer = services.GetRequiredService<ILocalizer>();

        // 5.5 — the stored language, applied on this single-threaded stretch before the pump exists,
        // which is what makes SetLanguage's UI-thread-only rule trivially true here.
        localizer.SetLanguage(settings.Current.Language);

        ReconcileBootDiagnostics(
            diagnostics.State,
            services.GetRequiredService<IConditionSink>(),
            services.GetRequiredService<IErrorSink>());

        // 6 — global hooks.
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 7 — closing a window never exits: only the tray's Exit item calls Shutdown (FR-08-4).
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _application = application;
        RegisterFatalExceptionHandlers(application);
        application.SessionEnding += (_, _) => FlushSettings(settings);

        var uiDispatcher = services.GetRequiredService<IUiDispatcher>();
        var uiTicker = services.GetRequiredService<IUiTicker>();
        var fanout = services.GetRequiredService<SnapshotFanout>();
        var overlayViewModel = services.GetRequiredService<OverlayViewModel>();
        var trayViewModel = services.GetRequiredService<TrayViewModel>();
        var settingsWindows = services.GetRequiredService<SettingsWindowFactory>();

        // 8 — the signal receiver, which is also the reachability fallback (D-SH14). A message-only
        // window can exist now; the overlay HWND does not until step 9.
        var signal = new InstanceSignal(
            uiDispatcher,
            services.GetRequiredService<MessageOnlyWindowFactory>(),
            settingsWindows.ShowAndActivate,
            services.GetRequiredService<ILogger<InstanceSignal>>());
        signal.StartReceiving();

        // 9 — the overlay. A raw layered parent hosting a WPF child, not a Window (S3 4.0 D-SH20).
        var overlay = services.GetRequiredService<OverlayHost>();
        overlay.Show();
        fanout.Attach(overlayViewModel);
        fanout.Attach(trayViewModel);
        var displayWatcher = services.GetRequiredService<DisplayChangeWatcher>();
        var modeService = services.GetRequiredService<OverlayModeService>();

        // 10 — the tray icon, checked rather than assumed.
        var trayHost = services.GetRequiredService<TrayIconHost>();
        _trayHost = trayHost;
        var trayRegistered = trayHost.TryRegister();

        if (!trayRegistered || FirstRunGate.ShouldAutoShowSettings(settings.Current))
        {
            // With no tray there is no other visible surface, and on a first run the guidance is
            // the point (D18-c, FR-08-6). Show() before Run() only queues the window.
            settingsWindows.GetOrCreate().Show();
        }

        // The 30 s tick, started here and stopped in teardown step a. Without it every derived
        // condition freezes precisely when polling dies — the moment they exist to report
        // (S3 3.2 B4, 9.1).
        uiTicker.Start(ShellConstants.UiTickPeriod);

        // 11 — no main window is passed: there is no Window to pass. ShutdownMode is
        // OnExplicitShutdown, so the pump runs until the tray's Exit item calls Shutdown (FR-08-4).
        _ = application.Run();

        // 12
        RunShutdownSequence(host, trayHost, guard, signal, settings, fanout, overlayViewModel, trayViewModel, uiTicker, displayWatcher, modeService, overlay, diagnostics);
        return 0;
    }

    /// <summary>
    /// Signals the running instance and reports honestly when it does not answer.
    /// </summary>
    /// <param name="paths">Used for the log folder in the dialog.</param>
    /// <returns>2 when the running instance acknowledged, 3 when it did not.</returns>
    /// <remarks>
    /// The two outcomes carry different exit codes deliberately. Collapsing them makes the one
    /// interesting failure — the running instance is there but the handler never ran — invisible to
    /// anything watching from outside the process, which is exactly the silence D-SH18 exists to
    /// break.
    /// </remarks>
    private static int HandOverToRunningInstance(AppPaths paths)
    {
        var result = InstanceSignal.TrySend(
            ShellConstants.SignalWindowClassName,
            ShellConstants.SendAttemptTimeout,
            ShellConstants.SendAttempts);

        _logger?.LogInformation("Second instance handed over: {Result}.", result);

        if (result == InstanceSignalSendResult.Acknowledged)
        {
            return 2;
        }

        // Not "unreachable": a receiver busy inside the handler produces exactly this signal and
        // then raises its settings window a few seconds later (S3 3.2 M6).
        _ = System.Windows.Forms.MessageBox.Show(
            string.Format(CultureInfo.CurrentCulture, NativeDialogText.InstanceUnreachable, paths.LogDirectory),
            "PoE Market Price Tracker",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Information);
        return 3;
    }

    /// <summary>
    /// Reflects boot-time findings once the Store can accept them (S3 3.1 P5, 3.2 M10).
    /// </summary>
    /// <param name="state">What boot learned.</param>
    /// <param name="conditionSink">The Store's condition face.</param>
    /// <param name="errorSink">The Store's error face.</param>
    private static void ReconcileBootDiagnostics(
        DiagnosticsStartupState state,
        IConditionSink conditionSink,
        IErrorSink errorSink)
    {
        if (state.LoggerOpenFailed)
        {
            errorSink.Report(new ErrorRecord(
                DateTimeOffset.UtcNow,
                "Diagnostics",
                "LoggingUnavailable",
                "ui.error.generic",
                "log file could not be opened",
                null,
                null,
                null,
                null));
            conditionSink.Set(AppConditionKind.LoggingUnavailable, true, "log file could not be opened");
        }

        if (state.SettingsFlushFailureTracePath is not { } trace)
        {
            return;
        }

        errorSink.Report(new ErrorRecord(
            DateTimeOffset.UtcNow,
            "Settings",
            "SettingsWriteFailed",
            "ui.error.settingsWriteFailed",
            trace,
            null,
            null,
            null,
            null));

        // Reusing SettingsWriteFailed rather than minting a condition: the failure was literally a
        // settings write failure, and it clears the same way, on the next successful write.
        conditionSink.Set(AppConditionKind.SettingsWriteFailed, true, "shutdown flush failed");

        // Only now — deleting at detection would lose the evidence if the Store never started
        // (S3 3.2 M2).
#pragma warning disable CA1031 // A trace we cannot delete is a repeated notice, not a crash.
        try
        {
            File.Delete(trace);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not delete the flush-failure trace {Path}.", trace);
        }
#pragma warning restore CA1031
    }

    /// <summary>Subscribes both fatal-exception channels (S4 12.1 B6).</summary>
    /// <param name="app">The application whose dispatcher exceptions are watched.</param>
    private static void RegisterFatalExceptionHandlers(Application app)
        => app.DispatcherUnhandledException += OnDispatcherUnhandledException;

    /// <summary>
    /// Teardown a–f (HLD 3.5 step 12 / S3 3.3).
    /// </summary>
    /// <remarks>
    /// The two orderings that carry weight: signal reception is switched off in (a), before the
    /// mutex is released in (d), so a relaunch during a slow stop cannot reach a process that is
    /// already dismantling itself; and the mutex is released before <c>StopAsync</c>, so a relaunch
    /// during that same slow stop can take the mutex rather than fall through to a channel with
    /// nobody on it.
    /// </remarks>
    private static void RunShutdownSequence(
        IHost host,
        TrayIconHost trayHost,
        SingleInstanceGuard guard,
        InstanceSignal signal,
        ISettingsSource settings,
        SnapshotFanout fanout,
        OverlayViewModel overlayViewModel,
        TrayViewModel trayViewModel,
        IUiTicker uiTicker,
        DisplayChangeWatcher displayWatcher,
        OverlayModeService modeService,
        OverlayHost overlay,
        BootDiagnostics diagnostics)
    {
        if (Interlocked.Exchange(ref _teardownStarted, 1) != 0)
        {
            return;
        }

        // a — stop everything that could still touch a disposed object.
        signal.StopReceiving();
        uiTicker.Stop();
        fanout.Detach(overlayViewModel);
        fanout.Detach(trayViewModel);
        displayWatcher.Dispose();
        modeService.Dispose();

        // The overlay's parent HWND, its class registration and its colour-key brush are this
        // process's, not the framework's: no Window means nothing else closes them.
        overlay.Dispose();

        // b — before (c), so a write failure still has a surface to be reported on.
        FlushSettings(settings);

        // c
        trayHost.Dispose();

        // d — before (e). See the remarks.
        guard.Release();
        signal.Dispose();

#pragma warning disable CA1031 // Nothing after this point may prevent (f).
        try
        {
            // e
            host.StopAsync(ShellConstants.ShutdownTimeout).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Host shutdown failed.");
        }
#pragma warning restore CA1031

        host.Dispose();

        // f — last, so every line above is already in the channel.
        diagnostics.Sink.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void FlushSettings(ISettingsSource settings)
    {
#pragma warning disable CA1031 // A failed flush is recorded, not thrown, during shutdown.
        try
        {
            var flush = Task.Run(() => settings.FlushAsync(CancellationToken.None));
            if (!flush.Wait(ShellConstants.ShutdownTimeout))
            {
                _logger?.LogWarning("Settings flush did not complete within the shutdown budget.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Settings flush failed during shutdown.");
        }
#pragma warning restore CA1031
    }

    private static void RequestShutdown() => _application?.Shutdown();

    /// <summary>
    /// The allow list is the empty set (S3 10.2 D-SH13).
    /// </summary>
    /// <remarks>
    /// Not one plausible harmless exception type has ever actually been named in this design — only
    /// the assumption that some exist. Listing guesses would hide real defects on paths that
    /// otherwise work. NFR-03 is unaffected: network failure is absorbed long before the UI thread,
    /// by Polling's outermost <c>finally</c>, Market's boundary catch and
    /// <c>BackgroundServiceExceptionBehavior.Ignore</c>. What reaches here is a programming error.
    /// </remarks>
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "Unhandled exception on the UI thread.");
        _trayHost?.Dispose();

        // Handled stays false: WPF rethrows, the process ends through its normal path, and
        // AppDomain.UnhandledException becomes the final receiver. The idempotent Dispose above
        // makes the double call safe.
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception; the process is terminating.");
        _trayHost?.Dispose();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Recording only. This is not a safety net — it fires at a garbage collection, long after
        // the fact (HLD 3.5 step 6).
        _logger?.LogError(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
