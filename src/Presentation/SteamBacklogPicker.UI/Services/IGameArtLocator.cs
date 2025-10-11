namespace SteamBacklogPicker.UI.Services;

public interface IGameArtLocator
{
    string? FindHeroImage(uint appId);
}
