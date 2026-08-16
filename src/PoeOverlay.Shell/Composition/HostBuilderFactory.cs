using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PoeOverlay.Composition;

/// <summary>
/// Builds the generic host (HLD 3.5 step 4 / S4 12.1).
/// </summary>
internal static class HostBuilderFactory
{
    /// <summary>Configures and builds the host.</summary>
    /// <param name="args">Command-line arguments, passed through unchanged.</param>
    /// <param name="paths">Resolved application folders.</param>
    /// <param name="diagnostics">The already-open logging objects from boot step 1.</param>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    /// <param name="requestShutdown">The single caller of <c>Application.Shutdown()</c>.</param>
    /// <returns>An unstarted host.</returns>
    internal static IHost Build(
        string[] args,
        AppPaths paths,
        BootDiagnostics diagnostics,
        Dispatcher dispatcher,
        Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(requestShutdown);

        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(diagnostics.Provider);

        builder.Services.Configure<HostOptions>(options =>
        {
            // A background service that faults must not take the host with it (HLD D12). This says
            // "the host survives", not "the service restarts" — the generic host never re-runs a
            // completed BackgroundService, which is why PollingStopped's LoopExited branch clears
            // only on an application restart (S3 2.2 D-SH2).
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;

            // Sequential, because the registration order is the stop order and the stop order is
            // load-bearing (see ServiceRegistration).
            options.ServicesStartConcurrently = false;
            options.ServicesStopConcurrently = false;
            options.ShutdownTimeout = ShellConstants.ShutdownTimeout;
        });

        // No ConsoleLifetime: this process has no console, and its lifetime is the WPF message loop.
        builder.Services.AddSingleton<IHostLifetime, NoopHostLifetime>();

        builder.Services.AddPoeOverlayCore(paths, diagnostics);
        builder.Services.AddPoeOverlayShell(dispatcher, paths, requestShutdown);

        return builder.Build();
    }

    /// <summary>A lifetime that neither installs signal handlers nor writes to a console.</summary>
    private sealed class NoopHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
