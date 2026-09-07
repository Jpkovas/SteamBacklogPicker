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
        // WPF selects the available rendering tier and falls back when acceleration is unavailable.
        // Keep an explicit compatibility override for problematic drivers or remote desktops.
        if (string.Equals(Environment.GetEnvironmentVariable("SBP_SOFTWARE_RENDERING"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SBP_HARDWARE_RENDERING"), "0", StringComparison.Ordinal))
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
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
        _updateCancellation?.Dispose();
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

        // Singleton services are owned and disposed once by the container.
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
