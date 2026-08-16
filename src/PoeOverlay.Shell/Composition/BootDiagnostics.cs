using System.IO;
using Microsoft.Extensions.Logging;
using PoeOverlay.Core.Diagnostics;
using PoeOverlay.Core.Settings;

namespace PoeOverlay.Composition;

/// <summary>
/// Step 1 of the boot sequence: the log file, opened before the container exists (HLD 3.5).
/// </summary>
/// <remarks>
/// Everything after this point can be recorded, which is the whole reason it comes first. What it
/// learns — that the logger would not open, that the last shutdown failed to save settings — cannot
/// be reported yet, because the Store that accepts conditions is registration step 6. So it is held
/// in <see cref="State"/> and reconciled exactly once, right after <c>Store.StartAsync</c> returns
/// (S3 3.1 P5).
/// </remarks>
internal sealed class BootDiagnostics : IAsyncDisposable
{
    private BootDiagnostics(
        RollingFileSink sink,
        RecentErrorRing errorRing,
        SessionSuppressionRegistry suppression,
        FileLoggerProvider provider,
        ILogger logger,
        DiagnosticsStartupState state)
    {
        Sink = sink;
        ErrorRing = errorRing;
        Suppression = suppression;
        Provider = provider;
        Logger = logger;
        State = state;
    }

    /// <summary>The rolling file sink.</summary>
    internal RollingFileSink Sink { get; }

    /// <summary>The recent-error ring the settings window reads.</summary>
    internal RecentErrorRing ErrorRing { get; }

    /// <summary>The once-per-session suppression channels.</summary>
    internal SessionSuppressionRegistry Suppression { get; }

    /// <summary>The provider handed to the host's logging builder.</summary>
    internal FileLoggerProvider Provider { get; }

    /// <summary>A logger usable before the host exists.</summary>
    internal ILogger Logger { get; }

    /// <summary>What boot learned before the Store could be told.</summary>
    internal DiagnosticsStartupState State { get; }

    /// <summary>Opens the log file and looks for a shutdown flush-failure trace.</summary>
    /// <param name="paths">Where the files live.</param>
    /// <returns>The opened diagnostics.</returns>
    internal static BootDiagnostics Open(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var sink = new RollingFileSink(paths.LogDirectory, new LogLineFormatter(), TimeProvider.System);
        var ring = new RecentErrorRing();
        var provider = new FileLoggerProvider(sink, ring, TimeProvider.System);
        var logger = provider.CreateLogger("Composition");

#pragma warning disable CA1031 // If the logger itself cannot open, that is the state being captured.
        try
        {
            sink.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // LoggingUnavailable is set by the sink itself; nothing else can be done here.
        }
#pragma warning restore CA1031

        var tracePath = Path.Combine(paths.AppDataDirectory, SettingsStore.FlushFailureTraceFileName);
        var state = new DiagnosticsStartupState
        {
            LoggerOpenFailed = sink.LoggingUnavailable,
            SettingsFlushFailureTracePath = File.Exists(tracePath) ? tracePath : null,
        };

        return new BootDiagnostics(sink, ring, provider, logger, state);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Provider.Dispose();
        await Sink.DisposeAsync().ConfigureAwait(false);
    }

    private BootDiagnostics(
        RollingFileSink sink,
        RecentErrorRing errorRing,
        FileLoggerProvider provider,
        ILogger logger,
        DiagnosticsStartupState state)
        : this(sink, errorRing, new SessionSuppressionRegistry(logger), provider, logger, state)
    {
    }
}
