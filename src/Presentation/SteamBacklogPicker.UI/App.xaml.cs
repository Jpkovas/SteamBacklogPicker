using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Domain.Selection;
using Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using SteamBacklogPicker.UI.Services.GameArt;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Library;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.Composition;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.Services.Runtime;
using SteamBacklogPicker.UI.Services.Updates;
using SteamBacklogPicker.UI.ViewModels;
using SteamClientAdapter;
using SteamDiscovery;
using ValveFormatParser;

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

        if (_serviceProvider.GetService<ILocalizationService>() is { } localizationService)
        {
            localizationService.ResourcesChanged += OnLocalizationResourcesChanged;
            OnLocalizationResourcesChanged(this, localizationService.GetAllStrings());
        }

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

        if (_serviceProvider.GetService<ILocalizationService>() is { } localizationService)
        {
            localizationService.ResourcesChanged -= OnLocalizationResourcesChanged;
        }

        if (_serviceProvider.GetService<ITelemetryClient>() is { } telemetryClient)
        {
            telemetryClient.TrackEvent("application_exited");
        }

        if (_serviceProvider.GetService<SteamAppManifestCache>() is { } cache)
        {
            cache.Dispose();
        }

        if (_serviceProvider.GetService<ISteamClientAdapter>() is IDisposable adapter)
        {
            adapter.Dispose();
        }

        _serviceProvider.Dispose();
        TelemetryBootstrapper.Shutdown();
    }

    private static void OnLocalizationResourcesChanged(object? _, IReadOnlyDictionary<string, string> resources)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        foreach (var (key, value) in resources)
        {
            app.Resources[key] = value;
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddTelemetryInfrastructure(options =>
        {
            options.ApplicationName = "SteamBacklogPicker";
            options.MinimumLogLevel = LogEventLevel.Information;
            options.TelemetryEnabledByDefault = false;
        });

        services.AddSingleton<ValveTextVdfParser>();
        services.AddSingleton<ValveBinaryVdfParser>();
        services.AddSingleton<IEnvironmentProvider, SystemEnvironmentProvider>();
        services.AddSingleton<IFileSystem, SystemFileSystem>();
        services.AddSingleton<IPlatformProvider, RuntimePlatformProvider>();
        services.AddSingleton<IWindowsSteamInstallPathProvider, WindowsSteamInstallPathProvider>();
        services.AddSingleton<ILinuxSteamInstallPathProvider, LinuxSteamInstallPathProvider>();
        services.AddSingleton<ISteamInstallPathProvider, DefaultSteamInstallPathProvider>();
        services.AddSingleton<ISteamLibraryFoldersParser, SteamLibraryFoldersParser>();
        services.AddSingleton<ISteamLibraryLocator, SteamLibraryLocator>();
        services.AddSingleton<IFileAccessor, DefaultFileAccessor>();

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
        services.AddSingleton<IGameLibraryService, CombinedGameLibraryService>();
        services.AddSingleton<IGameArtLocator, SteamGameArtLocator>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        services.AddPlatformUserExperienceServices();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
