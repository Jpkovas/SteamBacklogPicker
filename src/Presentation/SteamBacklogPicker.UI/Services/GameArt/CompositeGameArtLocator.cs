using System;
using Domain;

namespace SteamBacklogPicker.UI.Services.GameArt;

public sealed class CompositeGameArtLocator : IGameArtLocator
{
    private readonly SteamGameArtLocator steamLocator;
    private readonly EpicGameArtLocator epicLocator;

    public CompositeGameArtLocator(SteamGameArtLocator steamLocator, EpicGameArtLocator epicLocator)
    {
        this.steamLocator = steamLocator ?? throw new ArgumentNullException(nameof(steamLocator));
        this.epicLocator = epicLocator ?? throw new ArgumentNullException(nameof(epicLocator));
    }

    public string? FindHeroImage(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return game.Id.Storefront switch
        {
            Storefront.Steam => steamLocator.FindHeroImage(game),
            Storefront.EpicGamesStore => epicLocator.FindHeroImage(game),
            _ => null,
        };
    }
}
