using Domain.Selection;
using Microsoft.Extensions.DependencyInjection;
using SteamBacklogPicker.UI.Services.GameArt;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Library;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.Services.Runtime;
using SteamBacklogPicker.UI.ViewModels;
using SteamClientAdapter;
using SteamDiscovery;
using ValveFormatParser;

namespace SteamBacklogPicker.Linux.Composition;

public static class LinuxServiceCollectionExtensions
{
    public static IServiceCollection AddLinuxApplicationServices(this IServiceCollection services)
    {
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

        return services;
    }
}
