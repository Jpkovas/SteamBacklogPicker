using System;
using System.Collections.Generic;
using System.Linq;
using Domain;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class GameDetailsViewModel : ObservableObject
{
    private bool _isFavorite;

    private GameDetailsViewModel(
        uint appId,
        string title,
        string? coverImagePath,
        InstallState installState,
        OwnershipType ownershipType,
        IReadOnlyList<string> tags,
        bool isFavorite)
    {
        AppId = appId;
        Title = title;
        CoverImagePath = coverImagePath;
        InstallState = installState;
        OwnershipType = ownershipType;
        Tags = tags;
        _isFavorite = isFavorite;
    }

    public static GameDetailsViewModel Empty { get; } = new(
        0,
        "Nenhum jogo selecionado",
        null,
        InstallState.Unknown,
        OwnershipType.Unknown,
        Array.Empty<string>(),
        false);

    public uint AppId { get; }

    public string Title { get; }

    public string? CoverImagePath { get; }

    public InstallState InstallState { get; }

    public OwnershipType OwnershipType { get; }

    public IReadOnlyList<string> Tags { get; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public bool CanLaunch => InstallState == InstallState.Installed;

    public bool CanInstall => InstallState is InstallState.Available or InstallState.Shared;

    public string InstallationStatus => InstallState switch
    {
        InstallState.Installed => "Instalado",
        InstallState.Available => OwnershipType == OwnershipType.FamilyShared
            ? "Disponível via compartilhamento familiar"
            : "Disponível para instalar",
        InstallState.Shared => "Disponível via compartilhamento familiar",
        InstallState.Unknown => "Estado de instalação desconhecido",
        _ => "Estado de instalação desconhecido",
    };

    public static GameDetailsViewModel FromGame(GameEntry game, string? coverPath, bool isFavorite)
    {
        ArgumentNullException.ThrowIfNull(game);
        var tags = game.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                   ?? Array.Empty<string>();
        return new GameDetailsViewModel(
            game.AppId,
            game.Title,
            coverPath,
            game.InstallState,
            game.OwnershipType,
            tags,
            isFavorite);
    }
}
