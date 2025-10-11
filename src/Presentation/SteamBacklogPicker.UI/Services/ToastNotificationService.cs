using System;
using System.IO;
using CommunityToolkit.WinUI.Notifications;
using Domain;
using Windows.UI.Notifications;

namespace SteamBacklogPicker.UI.Services;

public sealed class ToastNotificationService : IToastNotificationService
{
    public void ShowGameSelected(GameEntry game, string? imagePath)
    {
        ArgumentNullException.ThrowIfNull(game);

        try
        {
            var builder = new ToastContentBuilder()
                .AddText("Jogo sorteado!")
                .AddText(game.Title);

            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                builder.AddInlineImage(new Uri(imagePath));
            }

            var toastContent = builder.GetToastContent();
            var notification = new ToastNotification(toastContent.GetXml())
            {
                ExpirationTime = DateTimeOffset.Now.AddMinutes(5),
            };

            ToastNotificationManagerCompat.CreateToastNotifier().Show(notification);
        }
        catch
        {
            // Toast notifications are optional; swallow exceptions in environments that do not support them.
        }
    }
}
