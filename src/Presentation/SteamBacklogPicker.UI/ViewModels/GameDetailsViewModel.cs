using System;
using System.Collections.Generic;
using System.Linq;
using Domain;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Localization;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class GameDetailsViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private readonly bool _isPlaceholder;
    private GameLaunchOptions _launchOptions;
    private string _title;

    private GameDetailsViewModel(
        ILocalizationService localizationService,
        GameIdentifier id,
        string title,
        string? coverImagePath,
        InstallState installState,
        OwnershipType ownershipType,
        IReadOnlyList<string> tags,
        GameLaunchOptions launchOptions,
        bool isPlaceholder)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        Id = id;
        _title = title;
        CoverImagePath = coverImagePath;
        InstallState = installState;
        OwnershipType = ownershipType;
        Tags = tags;
        _launchOptions = launchOptions ?? GameLaunchOptions.Empty;
        _isPlaceholder = isPlaceholder;
    }

    public static GameDetailsViewModel CreateEmpty(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        return new GameDetailsViewModel(
            localizationService,
            GameIdentifier.Unknown,
            localizationService.GetString("GameDetails_NoSelectionTitle"),
            null,
            InstallState.Unknown,
            OwnershipType.Unknown,
            Array.Empty<string>(),
            GameLaunchOptions.Empty,
            true);
    }

    public GameIdentifier Id { get; }

    public uint? SteamAppId => Id.SteamAppId;

    public Storefront Storefront => Id.Storefront;

    public bool HasStorefront => Storefront != Storefront.Unknown;

    public string StorefrontDisplayName => Storefront switch
    {
        Storefront.Steam => _localizationService.GetString("Storefront_Steam"),
        Storefront.EpicGamesStore => _localizationService.GetString("Storefront_Epic"),
        _ => _localizationService.GetString("Storefront_Unknown"),
    };

    public string StorefrontGlyph => Storefront switch
    {
        Storefront.Steam => "🟦",
        Storefront.EpicGamesStore => "🟪",
        _ => string.Empty,
    };

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string? CoverImagePath { get; }

    public InstallState InstallState { get; }

    public OwnershipType OwnershipType { get; }

    public IReadOnlyList<string> Tags { get; }

    public bool CanLaunch => _launchOptions.Launch.IsSupported;

    public bool CanInstall => _launchOptions.Install.IsSupported;

    public string? LaunchUri => _launchOptions.Launch.ProtocolUri;

    public string? InstallUri => _launchOptions.Install.ProtocolUri;

    public string? LaunchErrorMessage => _launchOptions.Launch.ErrorMessage;

    public string? InstallErrorMessage => _launchOptions.Install.ErrorMessage;

    public string? EpicAppName => _launchOptions.EpicAppName;

    public string? EpicCatalogItemId => _launchOptions.EpicCatalogItemId;

    public string? EpicCatalogNamespace => _launchOptions.EpicCatalogNamespace;

    public string InstallationStatus => InstallState switch
    {
        InstallState.Installed => _localizationService.GetString("GameDetails_InstallState_Installed"),
        InstallState.Available => OwnershipType == OwnershipType.FamilyShared
            ? _localizationService.GetString("GameDetails_InstallState_FamilySharing")
            : _localizationService.GetString("GameDetails_InstallState_Available"),
        InstallState.Shared => _localizationService.GetString("GameDetails_InstallState_FamilySharing"),
        InstallState.Unknown => _localizationService.GetString("GameDetails_InstallState_Unknown"),
        _ => _localizationService.GetString("GameDetails_InstallState_Unknown"),
    };

    public void RefreshLocalization()
    {
        if (_isPlaceholder)
        {
            Title = _localizationService.GetString("GameDetails_NoSelectionTitle");
        }

        OnPropertyChanged(nameof(InstallationStatus));
        OnPropertyChanged(nameof(StorefrontDisplayName));
    }

    internal GameLaunchOptions LaunchOptions => _launchOptions;

    internal void UpdateLaunchOptions(GameLaunchOptions launchOptions)
    {
        _launchOptions = launchOptions ?? GameLaunchOptions.Empty;
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(LaunchUri));
        OnPropertyChanged(nameof(InstallUri));
        OnPropertyChanged(nameof(LaunchErrorMessage));
        OnPropertyChanged(nameof(InstallErrorMessage));
        OnPropertyChanged(nameof(EpicAppName));
        OnPropertyChanged(nameof(EpicCatalogItemId));
        OnPropertyChanged(nameof(EpicCatalogNamespace));
    }

    public static GameDetailsViewModel FromGame(
        GameEntry game,
        string? coverPath,
        ILocalizationService localizationService,
        GameLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(localizationService);
        ArgumentNullException.ThrowIfNull(launchOptions);
        var tags = game.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                   ?? Array.Empty<string>();
        return new GameDetailsViewModel(
            localizationService,
            game.Id,
            game.Title,
            coverPath,
            game.InstallState,
            game.OwnershipType,
            tags,
            launchOptions,
            false);
    }
}
