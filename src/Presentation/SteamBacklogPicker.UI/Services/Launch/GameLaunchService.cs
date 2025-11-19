using System;
using Domain;
using EpicDiscovery;
using SteamBacklogPicker.UI.Services.Localization;

namespace SteamBacklogPicker.UI.Services.Launch;

/// <summary>
/// Provides storefront-specific launch and installation metadata for games.
/// </summary>
public sealed class GameLaunchService : IGameLaunchService
{
    private const string UnsupportedStorefrontKey = "GameLaunch_UnsupportedStorefront";
    private const string LaunchNotInstalledKey = "GameLaunch_LaunchNotInstalled";
    private const string SteamMissingAppIdKey = "GameLaunch_SteamMissingAppId";
    private const string SteamAlreadyInstalledKey = "GameLaunch_SteamAlreadyInstalled";
    private const string EpicMissingAppNameKey = "GameLaunch_EpicMissingAppName";
    private const string EpicAlreadyInstalledKey = "GameLaunch_EpicAlreadyInstalled";
    private const string EpicMissingCatalogItemKey = "GameLaunch_EpicMissingCatalogItem";

    private readonly ILocalizationService _localizationService;
    private readonly Func<GameIdentifier, EpicCatalogItem?>? epicCatalogLookup;

    public GameLaunchService(ILocalizationService localizationService)
        : this(localizationService, (Func<GameIdentifier, EpicCatalogItem?>?)null)
    {
    }

    public GameLaunchService(ILocalizationService localizationService, EpicMetadataCache? metadataCache)
        : this(localizationService, metadataCache is null
            ? null
            : new Func<GameIdentifier, EpicCatalogItem?>(metadataCache.GetCatalogEntry))
    {
    }

    public GameLaunchService(
        ILocalizationService localizationService,
        Func<GameIdentifier, EpicCatalogItem?>? epicCatalogLookup)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        this.epicCatalogLookup = epicCatalogLookup;
    }

    public GameLaunchOptions GetLaunchOptions(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return game.Id.Storefront switch
        {
            Storefront.Steam => BuildSteamOptions(game),
            Storefront.EpicGamesStore => BuildEpicOptions(game),
            Storefront.Unknown => BuildUnknownOptions(),
            _ => BuildUnknownOptions(),
        };
    }

    private GameLaunchOptions BuildSteamOptions(GameEntry game)
    {
        var appId = game.SteamAppId;
        if (!appId.HasValue)
        {
            var missingAppIdMessage = _localizationService.GetString(SteamMissingAppIdKey);
            return new GameLaunchOptions(
                GameLaunchAction.Unsupported(missingAppIdMessage),
                GameLaunchAction.Unsupported(missingAppIdMessage),
                null,
                null,
                null);
        }

        var launchAction = game.InstallState == InstallState.Installed
            ? GameLaunchAction.Supported($"steam://run/{appId.Value}")
            : GameLaunchAction.Unsupported(_localizationService.GetString(LaunchNotInstalledKey));

        var canInstall = game.InstallState is InstallState.Available or InstallState.Shared or InstallState.Unknown;
        var installAction = canInstall
            ? GameLaunchAction.Supported($"steam://install/{appId.Value}")
            : GameLaunchAction.Unsupported(_localizationService.GetString(SteamAlreadyInstalledKey));

        return new GameLaunchOptions(launchAction, installAction, null, null, null);
    }

    private GameLaunchOptions BuildEpicOptions(GameEntry game)
    {
        var catalogEntry = epicCatalogLookup?.Invoke(game.Id);
        var (appName, catalogItemId, catalogNamespace, productSlug) = ResolveEpicMetadata(game, catalogEntry);

        var launchAction = BuildEpicLaunchAction(game.InstallState, appName);
        var installAction = BuildEpicInstallAction(game.InstallState, catalogItemId, catalogNamespace, productSlug);

        return new GameLaunchOptions(launchAction, installAction, appName, catalogItemId, catalogNamespace);
    }

    private GameLaunchOptions BuildUnknownOptions()
    {
        var unsupported = GameLaunchAction.Unsupported(_localizationService.GetString(UnsupportedStorefrontKey));
        return new GameLaunchOptions(unsupported, unsupported, null, null, null);
    }

    private GameLaunchAction BuildEpicLaunchAction(InstallState installState, string? appName)
    {
        if (installState != InstallState.Installed)
        {
            return GameLaunchAction.Unsupported(_localizationService.GetString(LaunchNotInstalledKey));
        }

        if (string.IsNullOrWhiteSpace(appName))
        {
            return GameLaunchAction.Unsupported(_localizationService.GetString(EpicMissingAppNameKey));
        }

        var escapedAppName = Uri.EscapeDataString(appName);
        var protocol = $"com.epicgames.launcher://apps/{escapedAppName}?action=launch&silent=true";
        return GameLaunchAction.Supported(protocol);
    }

    private GameLaunchAction BuildEpicInstallAction(InstallState installState, string? catalogItemId, string? catalogNamespace, string? productSlug)
    {
        var canInstall = installState is InstallState.Available or InstallState.Shared or InstallState.Unknown;
        if (!canInstall)
        {
            return GameLaunchAction.Unsupported(_localizationService.GetString(EpicAlreadyInstalledKey));
        }

        if (string.IsNullOrWhiteSpace(catalogItemId))
        {
            return GameLaunchAction.Unsupported(_localizationService.GetString(EpicMissingCatalogItemKey));
        }

        var slug = BuildEpicProductSlug(catalogItemId, catalogNamespace, productSlug);
        var protocol = $"com.epicgames.launcher://store/product/{slug}?action=install";
        return GameLaunchAction.Supported(protocol);
    }

    private static string BuildEpicProductSlug(string catalogItemId, string? catalogNamespace, string? productSlug)
    {
        if (!string.IsNullOrWhiteSpace(productSlug))
        {
            return Uri.EscapeDataString(productSlug);
        }

        var encodedItemId = Uri.EscapeDataString(catalogItemId);
        if (string.IsNullOrWhiteSpace(catalogNamespace))
        {
            return encodedItemId;
        }

        var encodedNamespace = Uri.EscapeDataString(catalogNamespace);
        return $"{encodedNamespace}/{encodedItemId}";
    }

    private static (string? AppName, string? CatalogItemId, string? CatalogNamespace, string? ProductSlug) ResolveEpicMetadata(GameEntry game, EpicCatalogItem? catalogEntry)
    {
        var appName = Normalize(catalogEntry?.AppName);
        var catalogItemId = Normalize(catalogEntry?.CatalogItemId);
        var catalogNamespace = Normalize(catalogEntry?.CatalogNamespace);
        var productSlug = Normalize(catalogEntry?.ProductSlug);

        if (string.IsNullOrWhiteSpace(appName))
        {
            appName = TryInferAppName(game.Id.StoreSpecificId);
        }

        if (string.IsNullOrWhiteSpace(catalogItemId) || string.IsNullOrWhiteSpace(catalogNamespace))
        {
            var (inferredNamespace, inferredItemId) = SplitStoreSpecificId(game.Id.StoreSpecificId);
            catalogItemId ??= inferredItemId;
            catalogNamespace ??= inferredNamespace;
        }

        return (appName, catalogItemId, catalogNamespace, productSlug);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TryInferAppName(string storeSpecificId)
    {
        if (string.IsNullOrWhiteSpace(storeSpecificId))
        {
            return null;
        }

        return storeSpecificId.Contains(':', StringComparison.Ordinal)
            ? null
            : storeSpecificId;
    }

    private static (string? Namespace, string? ItemId) SplitStoreSpecificId(string storeSpecificId)
    {
        if (string.IsNullOrWhiteSpace(storeSpecificId))
        {
            return (null, null);
        }

        var parts = storeSpecificId.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return (parts[0], parts[1]);
        }

        return (null, parts.Length == 1 ? parts[0] : null);
    }
}
