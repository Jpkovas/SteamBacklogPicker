using Domain;

namespace SteamBacklogPicker.UI.Services;

public interface IToastNotificationService
{
    void ShowGameSelected(GameEntry game, string? imagePath);
}
