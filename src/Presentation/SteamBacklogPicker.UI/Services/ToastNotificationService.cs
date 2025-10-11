using System;
using System.IO;
using CommunityToolkit.WinUI.Notifications;
using Domain;

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

            builder.Show(toast => toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(5));
        }
        catch
        {
            // Toast notifications are optional; swallow exceptions in environments that do not support them.
        }
    }
}
