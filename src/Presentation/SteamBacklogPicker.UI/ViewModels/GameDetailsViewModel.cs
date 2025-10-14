using System;
using System.Collections.Generic;
using System.Linq;
using Domain;
using SteamBacklogPicker.UI.Services;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class GameDetailsViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private readonly bool _isPlaceholder;
    private string _title;

    private GameDetailsViewModel(
        ILocalizationService localizationService,
        uint appId,
        string title,
        string? coverImagePath,
        InstallState installState,
        OwnershipType ownershipType,
        IReadOnlyList<string> tags,
        bool isPlaceholder)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        AppId = appId;
        _title = title;
        CoverImagePath = coverImagePath;
        InstallState = installState;
        OwnershipType = ownershipType;
        Tags = tags;
        _isPlaceholder = isPlaceholder;
    }

    public static GameDetailsViewModel CreateEmpty(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        return new GameDetailsViewModel(
            localizationService,
            0,
            localizationService.GetString("GameDetails_NoSelectionTitle"),
            null,
            InstallState.Unknown,
            OwnershipType.Unknown,
            Array.Empty<string>(),
            true);
    }

    public uint AppId { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string? CoverImagePath { get; }

    public InstallState InstallState { get; }

    public OwnershipType OwnershipType { get; }

    public IReadOnlyList<string> Tags { get; }

    public bool CanLaunch => InstallState == InstallState.Installed;

    public bool CanInstall => InstallState is InstallState.Available or InstallState.Shared;

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
    }

    public static GameDetailsViewModel FromGame(GameEntry game, string? coverPath, ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(localizationService);
        var tags = game.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                   ?? Array.Empty<string>();
        return new GameDetailsViewModel(
            localizationService,
            game.AppId,
            game.Title,
            coverPath,
            game.InstallState,
            game.OwnershipType,
            tags,
            false);
    }
}
