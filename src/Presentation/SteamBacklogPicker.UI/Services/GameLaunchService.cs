using System;
using Domain;
using EpicDiscovery;

namespace SteamBacklogPicker.UI.Services;

/// <summary>
/// Provides storefront-specific launch and installation metadata for games.
/// </summary>
public sealed class GameLaunchService : IGameLaunchService
{
    private static readonly string GenericUnsupportedStorefrontMessage = "This storefront does not support launching from Steam Backlog Picker yet.";
    private static readonly string LaunchNotInstalledMessage = "Install the game before launching it.";
    private readonly Func<GameIdentifier, EpicCatalogItem?>? epicCatalogLookup;

    public GameLaunchService()
        : this((Func<GameIdentifier, EpicCatalogItem?>?)null)
    {
    }

    public GameLaunchService(EpicCatalogCache? epicCatalogCache)
        : this(epicCatalogCache?.GetCatalogEntry)
    {
    }

    public GameLaunchService(Func<GameIdentifier, EpicCatalogItem?>? epicCatalogLookup)
    {
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

    private static GameLaunchOptions BuildSteamOptions(GameEntry game)
    {
        var appId = game.SteamAppId;
        if (!appId.HasValue)
        {
            const string missingAppIdMessage = "The Steam app identifier is missing for this game.";
            return new GameLaunchOptions(
                GameLaunchAction.Unsupported(missingAppIdMessage),
                GameLaunchAction.Unsupported(missingAppIdMessage),
                null,
                null,
                null);
        }

        var launchAction = game.InstallState == InstallState.Installed
            ? GameLaunchAction.Supported($"steam://run/{appId.Value}")
            : GameLaunchAction.Unsupported(LaunchNotInstalledMessage);

        var canInstall = game.InstallState is InstallState.Available or InstallState.Shared or InstallState.Unknown;
        var installAction = canInstall
            ? GameLaunchAction.Supported($"steam://install/{appId.Value}")
            : GameLaunchAction.Unsupported("The game is already installed via Steam.");

        return new GameLaunchOptions(launchAction, installAction, null, null, null);
    }

    private GameLaunchOptions BuildEpicOptions(GameEntry game)
    {
        var catalogEntry = epicCatalogLookup?.Invoke(game.Id);
        var (appName, catalogItemId, catalogNamespace) = ResolveEpicMetadata(game, catalogEntry);

        var launchAction = BuildEpicLaunchAction(game.InstallState, appName);
        var installAction = BuildEpicInstallAction(game.InstallState, catalogItemId, catalogNamespace);

        return new GameLaunchOptions(launchAction, installAction, appName, catalogItemId, catalogNamespace);
    }

    private static GameLaunchOptions BuildUnknownOptions()
    {
        var unsupported = GameLaunchAction.Unsupported(GenericUnsupportedStorefrontMessage);
        return new GameLaunchOptions(unsupported, unsupported, null, null, null);
    }

    private static GameLaunchAction BuildEpicLaunchAction(InstallState installState, string? appName)
    {
        if (installState != InstallState.Installed)
        {
            return GameLaunchAction.Unsupported(LaunchNotInstalledMessage);
        }

        if (string.IsNullOrWhiteSpace(appName))
        {
            return GameLaunchAction.Unsupported("Epic metadata is missing the application name required to launch this title.");
        }

        var escapedAppName = Uri.EscapeDataString(appName);
        var protocol = $"com.epicgames.launcher://apps/{escapedAppName}?action=launch&silent=true";
        return GameLaunchAction.Supported(protocol);
    }

    private static GameLaunchAction BuildEpicInstallAction(InstallState installState, string? catalogItemId, string? catalogNamespace)
    {
        var canInstall = installState is InstallState.Available or InstallState.Shared or InstallState.Unknown;
        if (!canInstall)
        {
            return GameLaunchAction.Unsupported("The game is already installed via the Epic Games Launcher.");
        }

        if (string.IsNullOrWhiteSpace(catalogItemId))
        {
            return GameLaunchAction.Unsupported("Epic metadata is missing the catalog item identifier required to install this title.");
        }

        var slug = BuildEpicProductSlug(catalogItemId, catalogNamespace);
        var protocol = $"com.epicgames.launcher://store/product/{slug}?action=install";
        return GameLaunchAction.Supported(protocol);
    }

    private static string BuildEpicProductSlug(string catalogItemId, string? catalogNamespace)
    {
        var encodedItemId = Uri.EscapeDataString(catalogItemId);
        if (string.IsNullOrWhiteSpace(catalogNamespace))
        {
            return encodedItemId;
        }

        var encodedNamespace = Uri.EscapeDataString(catalogNamespace);
        return $"{encodedNamespace}/{encodedItemId}";
    }

    private static (string? AppName, string? CatalogItemId, string? CatalogNamespace) ResolveEpicMetadata(GameEntry game, EpicCatalogItem? catalogEntry)
    {
        var appName = Normalize(catalogEntry?.AppName);
        var catalogItemId = Normalize(catalogEntry?.CatalogItemId);
        var catalogNamespace = Normalize(catalogEntry?.CatalogNamespace);

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

        return (appName, catalogItemId, catalogNamespace);
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
