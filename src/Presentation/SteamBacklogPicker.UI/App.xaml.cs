using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.UI.Services;
using SteamBacklogPicker.UI.ViewModels;
using SteamClientAdapter;
using SteamDiscovery;
using ValveFormatParser;
using Domain.Selection;

namespace SteamBacklogPicker.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _serviceProvider = BuildServices();
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        if (_serviceProvider is null)
        {
            return;
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
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ValveTextVdfParser>();
        services.AddSingleton<ValveBinaryVdfParser>();
        services.AddSingleton<ISteamRegistryReader, SteamRegistryReader>();
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
        services.AddSingleton<IGameLibraryService, SteamGameLibraryService>();
        services.AddSingleton<IGameArtLocator, SteamGameArtLocator>();
        services.AddSingleton<IToastNotificationService, ToastNotificationService>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
