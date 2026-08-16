using System.IO;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Domain.Ports;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Market;
using PoeOverlay.Core.Polling;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Presentation.Overlay;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Store;
using PoeOverlay.Interop;
using PoeOverlay.Overlay;
using PoeOverlay.Tray;
using StoreService = PoeOverlay.Core.Store.Store;

namespace PoeOverlay.Composition;

/// <summary>
/// The registration table of S3 3.1, in order (S4 12.1).
/// </summary>
/// <remarks>
/// The order is not cosmetic: the generic host stops hosted services in reverse registration order,
/// so <c>Store</c> before <c>Polling</c> is what puts <c>Polling.StopAsync</c> first and lets its
/// outermost <c>finally</c> — D20's final heartbeat write, and the <c>LoopExited</c> record — land
/// while the Store's command channel is still open. Reversed, the Store closes the channel first,
/// the final <c>Post</c> fails its <c>TryWrite</c> check and the very signal D20 exists to preserve
/// disappears exactly on the path that produces it (S3 3.1).
/// </remarks>
internal static class ServiceRegistration
{
    /// <summary>Registers Domain through Presentation, in the S3 3.1 order.</summary>
    /// <param name="services">The collection.</param>
    /// <param name="paths">Resolved application folders.</param>
    /// <param name="diagnostics">The already-open logging objects from boot step 1.</param>
    /// <returns>The same collection.</returns>
    internal static IServiceCollection AddPoeOverlayCore(
        this IServiceCollection services,
        AppPaths paths,
        BootDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // 1 — Diagnostics. Opened before the container so that everything after it can be recorded.
        services.AddSingleton(diagnostics.Sink);
        services.AddSingleton(diagnostics.ErrorRing);
        services.AddSingleton(diagnostics.Suppression);

        // 2 — the shared clock. Nothing but UiTicker reads the wall clock directly (S2 1.3).
        services.AddSingleton(TimeProvider.System);

        // 3 — HTTP. MarketClient owns its own retry, timeout and backoff (S2 5.8), so the named
        // client carries identity only; the per-attempt timeout is applied by the caller.
        services.AddHttpClient(ShellConstants.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ShellConstants.UserAgent);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // 4 — Localization, before Settings: settings validation requires `language` to be one of
        // the discovered dictionaries, so the dictionary list has to exist first (HLD 3.5 step 5).
        services.AddSingleton(sp => new LocalizationCatalog(
            paths.LocalizationDirectory,
            sp.GetRequiredService<ILogger<LocalizationCatalog>>(),
            sp.GetRequiredService<SessionSuppressionRegistry>()));
        services.AddSingleton<ILocalizer>(sp => sp.GetRequiredService<LocalizationCatalog>());
        services.AddSingleton<ITemplateSource>(sp => sp.GetRequiredService<LocalizationCatalog>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<LocalizationCatalog>());

        // 5 — Settings.
        services.AddSingleton(sp => new SettingsStore(
            paths.AppDataDirectory,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IConditionSink>(),
            sp.GetRequiredService<IErrorSink>(),
            sp.GetRequiredService<ILogger<SettingsStore>>()));
        services.AddSingleton<ISettingsSource>(sp => sp.GetRequiredService<SettingsStore>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SettingsStore>());

        // 6 — Store, as five faces of one singleton (S3 3.1 B3).
        services.AddSingleton<StoreService>();
        services.AddSingleton<IMarketSnapshotSource>(sp => sp.GetRequiredService<StoreService>());
        services.AddSingleton<IConditionSink>(sp => sp.GetRequiredService<StoreService>());
        services.AddSingleton<IErrorSink>(sp => sp.GetRequiredService<StoreService>());
        services.AddSingleton<ISearchSource>(sp => sp.GetRequiredService<StoreService>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<StoreService>());

        // 7 — Market.
        services.AddSingleton<NinjaGateway>();
        services.AddSingleton<MarketClient>();
        services.AddSingleton<IMarketClient>(sp => sp.GetRequiredService<MarketClient>());

        // 8 — Polling, after the Store. See the type remarks.
        services.AddSingleton<PollingService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PollingService>());

        // 9 — the fan-out.
        services.AddSingleton<SnapshotFanout>();

        // 9′ — singletons, not transients. D18-b asks for transient view models because a window
        // kept alive refreshes invisible UI; the overlay and the tray are supposed to update while
        // unobserved, so the argument does not reach them (S3 3.1 B5).
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<TrayViewModel>();

        // 9″ — the one genuinely transient view model, with its window-scope token.
        services.AddSingleton<Func<CancellationToken, SettingsViewModel>>(sp => windowScope =>
            new SettingsViewModel(
                sp.GetRequiredService<ISearchSource>(),
                sp.GetRequiredService<IMarketClient>(),
                sp.GetRequiredService<ISettingsSource>(),
                sp.GetRequiredService<ILocalizer>(),
                sp.GetRequiredService<IOverlayModeService>(),
                sp.GetRequiredService<IOverlayGeometryService>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<RecentErrorRing>(),
                sp.GetRequiredService<TimeProvider>(),
                windowScope,
                (league, epoch, category, snapshot) =>
                    sp.GetRequiredService<StoreService>().SetFetchedListing(new DataTag(league, epoch), category, snapshot),
                ct => sp.GetRequiredService<TrayIconHost>().TryReregisterAsync(ct),
                list => sp.GetRequiredService<StoreService>().SetLeagueList(list),
                () => OpenLogFolder(paths.LogDirectory, sp.GetRequiredService<ILogger<SettingsStore>>()),
                sp.GetRequiredService<ILogger<SettingsViewModel>>()));

        return services;
    }

    /// <summary>Registers the Shell implementations (S3 3.1 row 10).</summary>
    /// <param name="services">The collection.</param>
    /// <param name="dispatcher">The UI thread's dispatcher — this is the STA thread that runs Main.</param>
    /// <param name="paths">Resolved application folders.</param>
    /// <param name="requestShutdown">The single caller of <c>Application.Shutdown()</c>.</param>
    /// <returns>The same collection.</returns>
    internal static IServiceCollection AddPoeOverlayShell(
        this IServiceCollection services,
        Dispatcher dispatcher,
        AppPaths paths,
        Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(requestShutdown);

        services.AddSingleton<IUiDispatcher>(_ => new UiDispatcher(dispatcher));
        services.AddSingleton<IUiTicker>(_ => new UiTicker(dispatcher));

        services.AddSingleton<ExtendedStyleGate.Factory>(_ => hwnd => new ExtendedStyleGate(hwnd));
        services.AddSingleton<MessageOnlyWindowFactory>();
        services.AddSingleton<LayeredHostWindowFactory>();

        services.AddSingleton(sp => new OverlayHost(
            sp.GetRequiredService<OverlayViewModel>(),
            sp.GetRequiredService<ExtendedStyleGate.Factory>(),
            sp.GetRequiredService<LayeredHostWindowFactory>(),
            sp.GetRequiredService<ISettingsSource>(),
            sp.GetRequiredService<ILogger<OverlayHost>>()));

        services.AddSingleton(sp => new OverlayModeService(
            sp.GetRequiredService<OverlayHost>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ISettingsSource>(),
            sp.GetRequiredService<ILogger<OverlayModeService>>()));
        services.AddSingleton<IOverlayModeService>(sp => sp.GetRequiredService<OverlayModeService>());

        services.AddSingleton(sp => new OverlayGeometryService(
            sp.GetRequiredService<OverlayHost>(),
            sp.GetRequiredService<ISettingsSource>()));
        services.AddSingleton<IOverlayGeometryService>(sp => sp.GetRequiredService<OverlayGeometryService>());

        services.AddSingleton(sp => new DisplayChangeWatcher(
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<OverlayModeService>()));

        services.AddSingleton(sp => new SettingsWindowFactory(
            sp.GetRequiredService<OverlayHost>(),
            sp.GetRequiredService<SnapshotFanout>(),
            sp.GetRequiredService<Func<CancellationToken, SettingsViewModel>>(),
            sp.GetRequiredService<ISettingsSource>(),
            sp.GetRequiredService<ILocalizer>(),
            sp.GetRequiredService<ILogger<SettingsWindowFactory>>()));

        services.AddSingleton(sp => new TrayIconHost(
            sp.GetRequiredService<TrayViewModel>(),
            sp.GetRequiredService<IConditionSink>(),
            sp.GetRequiredService<SettingsWindowFactory>(),
            sp.GetRequiredService<IOverlayModeService>(),
            sp.GetRequiredService<ILocalizer>(),
            sp.GetRequiredService<TimeProvider>(),
            requestShutdown,
            () => paths.LogDirectory,
            sp.GetRequiredService<ILogger<TrayIconHost>>()));

        return services;
    }

    private static void OpenLogFolder(string logDirectory, ILogger logger)
    {
#pragma warning disable CA1031 // Failing to open a folder must not take the settings window down.
        try
        {
            _ = Directory.CreateDirectory(logDirectory);
            using var process = Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open the log folder {Directory}.", logDirectory);
        }
#pragma warning restore CA1031
    }
}
