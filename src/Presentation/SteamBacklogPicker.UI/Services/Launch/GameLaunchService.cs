using System;
using Domain;
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

    private readonly ILocalizationService _localizationService;

    public GameLaunchService(ILocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public GameLaunchOptions GetLaunchOptions(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return game.Id.Storefront switch
        {
            Storefront.Steam => BuildSteamOptions(game),
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

    private GameLaunchOptions BuildUnknownOptions()
    {
        var unsupported = GameLaunchAction.Unsupported(_localizationService.GetString(UnsupportedStorefrontKey));
        return new GameLaunchOptions(unsupported, unsupported, null, null, null);
    }
}
