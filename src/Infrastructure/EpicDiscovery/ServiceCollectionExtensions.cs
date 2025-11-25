using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamClientAdapter;

namespace EpicDiscovery;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEpicDiscovery(this IServiceCollection services, Action<EpicLauncherLocatorOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddOptions<EpicLauncherLocatorOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<HttpClient>(_ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        });
        services.AddSingleton<IEpicLauncherLocator, EpicLauncherLocator>();
        services.AddSingleton<EpicManifestCache>();
        services.AddSingleton<EpicCatalogCache>();
        services.AddSingleton<EpicAuthenticationClient>();
        services.AddSingleton<EpicGraphQlClient>();
        services.AddSingleton<EpicHeroArtCache>(sp => new EpicHeroArtCache(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<IFileAccessor>(),
            sp.GetService<ILogger<EpicHeroArtCache>>()));
        services.AddSingleton<EpicMetadataFetcher>();
        services.AddSingleton<EpicMetadataCache>();
        services.AddSingleton<EpicEntitlementCache>();
        services.AddSingleton<IEpicGameLibrary, EpicGameLibrary>();

        return services;
    }
}
