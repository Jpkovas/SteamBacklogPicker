using Domain;

namespace SteamBacklogPicker.UI.Services.Notifications;

public interface IToastNotificationService
{
    void ShowGameSelected(GameEntry game, string? imagePath);
}
