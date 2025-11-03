using Domain;

namespace EpicDiscovery;

internal static class EpicIdentifierFactory
{
    public static GameIdentifier Create(string? catalogItemId, string? catalogNamespace, string? appName)
    {
        var storeSpecificId = ComposeStoreSpecificId(catalogItemId, catalogNamespace, appName);
        return new GameIdentifier
        {
            Storefront = Storefront.EpicGamesStore,
            StoreSpecificId = storeSpecificId
        };
    }

    private static string ComposeStoreSpecificId(string? catalogItemId, string? catalogNamespace, string? appName)
    {
        if (!string.IsNullOrWhiteSpace(catalogItemId))
        {
            if (!string.IsNullOrWhiteSpace(catalogNamespace))
            {
                return $"{catalogNamespace}:{catalogItemId}";
            }

            return catalogItemId!;
        }

        if (!string.IsNullOrWhiteSpace(appName))
        {
            return appName!;
        }

        return "unknown";
    }
}
