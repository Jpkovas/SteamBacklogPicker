using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows;
using Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using SteamBacklogPicker.AppCore.Composition;
using SteamBacklogPicker.UI.Composition;
using SteamBacklogPicker.UI.Services.Localization;
using SteamClientAdapter;
using SteamDiscovery;

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

        services.AddPlatformUserExperienceServices();
        services.AddSteamBacklogPickerApplicationCore();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
