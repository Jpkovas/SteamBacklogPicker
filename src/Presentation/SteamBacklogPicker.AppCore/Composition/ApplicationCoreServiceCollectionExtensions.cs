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
using SteamCatalog;

namespace SteamBacklogPicker.AppCore.Composition;

public static class ApplicationCoreServiceCollectionExtensions
{
    public static IServiceCollection AddSteamBacklogPickerApplicationCore(this IServiceCollection services)
    {
        services.AddSingleton<ValveTextVdfParser>();
        services.AddSingleton<ValveBinaryVdfParser>();
        services.AddSingleton<IEnvironmentProvider, SystemEnvironmentProvider>();
        services.AddSingleton<IFileSystem, SystemFileSystem>();
        services.AddSingleton<IPlatformProvider, RuntimePlatformProvider>();
        services.AddSingleton<IPathComparisonStrategy, PlatformPathComparisonStrategy>();
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
            var adapter = new SteamClientAdapter.SteamClientAdapter(
                sp.GetRequiredService<INativeLibraryLoader>(),
                sp.GetRequiredService<ISteamVdfFallback>());
            // Steamworks requires an application context. Local discovery works without loading game DLLs.
            return adapter;
        });

        services.AddSingleton<SteamAppManifestCache>();
        services.AddSingleton<ISelectionEngine>(_ => new SelectionEngine());
        services.AddSingleton<IGameLibraryProvider, SteamLibraryProvider>();
        services.AddSingleton<CombinedGameLibraryService>();
        services.AddSingleton<SqliteCatalogCache>();
        services.AddSingleton<SteamKitCatalogTransport>();
        services.AddSingleton<SteamStoreMetadataSource>();
        services.AddSingleton<ISteamCatalogService>(sp => new SteamCatalogService(sp.GetRequiredService<SqliteCatalogCache>(),
            new FallbackCatalogMetadataSource(sp.GetRequiredService<SteamKitCatalogTransport>(), sp.GetRequiredService<SteamStoreMetadataSource>())));
        services.AddSingleton<ISteamFamilySessionService>(sp => new SteamFamilySessionService(
            sp.GetRequiredService<SteamKitCatalogTransport>(), sp.GetRequiredService<SqliteCatalogCache>()));
        services.AddSingleton<IGameLibraryService>(sp => new CatalogLibraryService(sp.GetRequiredService<CombinedGameLibraryService>(),
            sp.GetRequiredService<ISteamVdfFallback>(), sp.GetRequiredService<ISteamCatalogService>(), sp.GetRequiredService<ISteamFamilySessionService>()));
        services.AddSingleton(_ => new BacklogStore(Path.Combine(Path.GetDirectoryName(SqliteCatalogCache.GetDefaultPath())!, "backlog.json")));
        services.AddSingleton<IGameArtLocator, SteamGameArtLocator>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        services.AddSingleton<MainViewModel>();

        return services;
    }
}
