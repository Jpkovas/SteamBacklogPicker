using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Infrastructure.Telemetry;
using SteamBacklogPicker.UI.Services;
using SteamBacklogPicker.UI.ViewModels;
using SteamClientAdapter;
using SteamDiscovery;
using ValveFormatParser;
using Domain.Selection;
using EpicDiscovery;

namespace SteamBacklogPicker.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private CancellationTokenSource? _updateCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _serviceProvider = BuildServices();
        _updateCancellation = new CancellationTokenSource();
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (_serviceProvider.GetService<ITelemetryClient>() is { } telemetryClient)
        {
            telemetryClient.TrackEvent("application_started", new Dictionary<string, object>
            {
                ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"
            });
        }

        if (_serviceProvider.GetService<IAppUpdateService>() is { } updateService)
        {
            _ = Task.Run(() => updateService.CheckForUpdatesAsync(_updateCancellation.Token));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        _updateCancellation?.Cancel();
        if (_serviceProvider is null)
        {
            return;
        }

        if (_serviceProvider.GetService<ITelemetryClient>() is { } telemetryClient)
        {
            telemetryClient.TrackEvent("application_exited");
        }

        if (_serviceProvider.GetService<SteamAppManifestCache>() is { } cache)
        {
            cache.Dispose();
        }

        if (_serviceProvider.GetService<EpicManifestCache>() is { } epicManifest)
        {
            epicManifest.Dispose();
        }

        if (_serviceProvider.GetService<EpicCatalogCache>() is { } epicCatalog)
        {
            epicCatalog.Dispose();
        }

        if (_serviceProvider.GetService<ISteamClientAdapter>() is IDisposable adapter)
        {
            adapter.Dispose();
        }

        _serviceProvider.Dispose();
        TelemetryBootstrapper.Shutdown();
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddTelemetryInfrastructure(options =>
        {
            options.ApplicationName = "SteamBacklogPicker";
            options.MinimumLogLevel = LogEventLevel.Information;
            options.TelemetryEnabledByDefault = false;
        });

        services.AddSingleton<ValveTextVdfParser>();
        services.AddSingleton<ValveBinaryVdfParser>();
        services.AddSingleton<ISteamRegistryReader, SteamRegistryReader>();
        services.AddSingleton<ISteamLibraryFoldersParser, SteamLibraryFoldersParser>();
        services.AddSingleton<ISteamLibraryLocator, SteamLibraryLocator>();
        services.AddSingleton<IFileAccessor, DefaultFileAccessor>();
        services.AddEpicDiscovery();
        services.AddSingleton<INativeLibraryLoader, DefaultNativeLibraryLoader>();
        services.AddSingleton<ISteamEnvironment, SteamEnvironment>();
        services.AddSingleton<ISteamVdfFallback>(sp =>
        {
            var environment = sp.GetRequiredService<ISteamEnvironment>();
            return new SteamVdfFallback(
                environment.GetSteamDirectory(),
                sp.GetRequiredService<IFileAccessor>(),
                sp.GetRequiredService<ValveTextVdfParser>(),
                sp.GetRequiredService<ValveBinaryVdfParser>());
        });
        services.AddSingleton<ISteamClientAdapter>(sp =>
        {
            var environment = sp.GetRequiredService<ISteamEnvironment>();
            var adapter = new SteamClientAdapter.SteamClientAdapter(
                sp.GetRequiredService<INativeLibraryLoader>(),
                sp.GetRequiredService<ISteamVdfFallback>());
            environment.TryInitializeSteamApi(adapter);
            return adapter;
        });
        services.AddSingleton<SteamAppManifestCache>();
        services.AddSingleton<ISelectionEngine>(_ => new SelectionEngine());
        services.AddSingleton<IGameLibraryProvider, SteamLibraryProvider>();
        services.AddSingleton<IGameLibraryProvider, EpicLibraryProvider>();
        services.AddSingleton<IGameLibraryService, CombinedGameLibraryService>();
        services.AddSingleton<IGameArtLocator, SteamGameArtLocator>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IToastNotificationService, ToastNotificationService>();
        services.AddSingleton<IAppUpdateService, SquirrelUpdateService>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
