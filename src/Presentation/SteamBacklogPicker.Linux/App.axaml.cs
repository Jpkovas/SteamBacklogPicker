using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Domain.Selection;
using SteamBacklogPicker.Linux.Services.Notifications;
using SteamBacklogPicker.Linux.Views;
using SteamBacklogPicker.UI.Services.GameArt;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Library;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.ViewModels;
using SteamClientAdapter;
using SteamDiscovery;
using ValveFormatParser;

namespace SteamBacklogPicker.Linux;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _serviceProvider = BuildServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
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
        services.AddSingleton<ISteamVdfFallback>(sp => new SteamVdfFallback(
            string.Empty,
            sp.GetRequiredService<IFileAccessor>(),
            sp.GetRequiredService<ValveTextVdfParser>(),
            sp.GetRequiredService<ValveBinaryVdfParser>()));
        services.AddSingleton<ISteamClientAdapter>(sp => new SteamClientAdapter.SteamClientAdapter(
            sp.GetRequiredService<INativeLibraryLoader>(),
            sp.GetRequiredService<ISteamVdfFallback>()));
        services.AddSingleton<ISelectionEngine>(_ => new SelectionEngine());
        services.AddSingleton<IGameLibraryProvider, SteamLibraryProvider>();
        services.AddSingleton<IGameLibraryService, CombinedGameLibraryService>();
        services.AddSingleton<IGameArtLocator, SteamGameArtLocator>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        services.AddSingleton<IToastNotificationService, LinuxToastNotificationService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
