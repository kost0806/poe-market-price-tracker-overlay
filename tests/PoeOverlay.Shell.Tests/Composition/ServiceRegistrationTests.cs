using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PoeOverlay.Composition;
using PoeOverlay.Core.Domain.Ports;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Polling;
using PoeOverlay.Core.Presentation.Fanout;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Settings;
using PoeOverlay.Core.Store;
using Xunit;
using StoreService = PoeOverlay.Core.Store.Store;

namespace PoeOverlay.Shell.Tests.Composition;

/// <summary>
/// The registration table of S3 3.1 — order and lifetimes.
/// </summary>
/// <remarks>
/// The registration order <em>is</em> the stop order, and the one thing that order buys is that
/// <c>Polling</c>'s outermost <c>finally</c> runs while the Store's command channel is still open.
/// Nothing at runtime complains when it is wrong: the final heartbeat simply never lands, and every
/// other indicator stays green. That is exactly the kind of defect worth a cheap structural test.
/// </remarks>
public sealed class ServiceRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poeoverlay-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly BootDiagnostics _diagnostics;

    public ServiceRegistrationTests()
    {
        var logs = Path.Combine(_root, "logs");
        _ = Directory.CreateDirectory(logs);
        _paths = new AppPaths(_root, logs, Path.Combine(_root, "Localization"));
        _diagnostics = BootDiagnostics.Open(_paths);
    }

    public void Dispose()
    {
        _diagnostics.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked log file is not a test failure.
        }
    }

    [Fact]
    public void HostedServices_AreRegisteredInTheBootOrder()
    {
        Assert.Equal(
            [nameof(LocalizationCatalog), nameof(SettingsStore), typeof(StoreService).Name, nameof(PollingService)],
            HostedServiceNames());
    }

    [Fact]
    public void LocalizationIsRegisteredBeforeSettings()
    {
        // Settings validation requires `language` to be one of the discovered dictionaries, so the
        // reverse order cannot work at all (HLD 3.5 step 5).
        var names = HostedServiceNames();
        Assert.True(names.IndexOf(nameof(LocalizationCatalog)) < names.IndexOf(nameof(SettingsStore)));
    }

    [Fact]
    public void StoreIsRegisteredBeforePolling()
    {
        var names = HostedServiceNames();
        Assert.True(names.IndexOf(typeof(StoreService).Name) < names.IndexOf(nameof(PollingService)));
    }

    [Fact]
    public void StoreIsOneSingletonWearingFiveFaces()
    {
        using var provider = BuildProvider();

        var store = provider.GetRequiredService<StoreService>();
        Assert.Same(store, provider.GetRequiredService<IMarketSnapshotSource>());
        Assert.Same(store, provider.GetRequiredService<IConditionSink>());
        Assert.Same(store, provider.GetRequiredService<IErrorSink>());

        // The fifth face is the one the first draft of the table left out; without it the settings
        // view model cannot be constructed and FR-01-1 dies silently (S3 3.1 B3).
        Assert.Same(store, provider.GetRequiredService<ISearchSource>());
        Assert.Contains(store, provider.GetServices<IHostedService>());
    }

    [Fact]
    public void OverlayAndTrayViewModelsAreSingletons()
    {
        var services = BuildCollection();

        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf(services, typeof(OverlayViewModel)));
        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf(services, typeof(TrayViewModel)));
    }

    [Fact]
    public void SettingsViewModelIsReachedThroughAFactory()
    {
        // The one genuinely transient view model. It is registered as a factory rather than as a
        // transient service because it needs the window-scope token, which DI cannot supply.
        var services = BuildCollection();
        Assert.Contains(services, d => d.ServiceType == typeof(Func<CancellationToken, SettingsViewModel>));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(SettingsViewModel));
    }

    [Fact]
    public void EverySharedServiceIsASingleton()
    {
        var services = BuildCollection();

        // Only this application's own registrations: the logging and options infrastructure has
        // scoped members of its own. A scoped or transient registration in *this* graph would
        // quietly duplicate state the design assumes is unique — two Stores, two fan-outs.
        var ours = services.Where(d =>
            (d.ServiceType.FullName?.StartsWith("PoeOverlay", StringComparison.Ordinal) ?? false)
            || (d.ImplementationType?.FullName?.StartsWith("PoeOverlay", StringComparison.Ordinal) ?? false));

        Assert.All(ours, d => Assert.Equal(ServiceLifetime.Singleton, d.Lifetime));
    }

    [Fact]
    public void SnapshotFanoutAndTheAdaptersAreRegistered()
    {
        var services = BuildCollection();

        Assert.Contains(services, d => d.ServiceType == typeof(SnapshotFanout));
        Assert.Contains(services, d => d.ServiceType == typeof(IUiDispatcher));
        Assert.Contains(services, d => d.ServiceType == typeof(IUiTicker));
    }

    private static ServiceLifetime LifetimeOf(IServiceCollection services, Type serviceType)
        => services.Single(d => d.ServiceType == serviceType).Lifetime;

    /// <summary>Resolves each IHostedService registration in order and names the concrete type.</summary>
    private List<string> HostedServiceNames()
    {
        var services = BuildCollection();
        using var provider = services.BuildServiceProvider();

        return services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => (d.ImplementationFactory?.Invoke(provider) ?? d.ImplementationInstance)!.GetType().Name)
            .ToList();
    }

    private IServiceCollection BuildCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _ = services.AddPoeOverlayCore(_paths, _diagnostics);
        _ = services.AddPoeOverlayShell(Dispatcher.CurrentDispatcher, _paths, () => { });
        return services;
    }

    private ServiceProvider BuildProvider() => BuildCollection().BuildServiceProvider();

}
