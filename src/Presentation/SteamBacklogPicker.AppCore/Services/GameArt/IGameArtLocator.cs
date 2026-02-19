using Domain;

namespace SteamBacklogPicker.UI.Services.GameArt;

public interface IGameArtLocator
{
    string? FindHeroImage(GameEntry game);
}
