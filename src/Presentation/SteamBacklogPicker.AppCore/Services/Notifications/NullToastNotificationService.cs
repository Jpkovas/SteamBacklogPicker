using Domain;

namespace SteamBacklogPicker.UI.Services.Notifications;

public sealed class NullToastNotificationService : IToastNotificationService
{
    public void ShowGameSelected(GameEntry game, string? imagePath)
    {
    }
}
