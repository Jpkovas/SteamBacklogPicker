using Domain;

namespace SteamBacklogPicker.UI.Services;

public interface IGameArtLocator
{
    string? FindHeroImage(GameEntry game);
}
