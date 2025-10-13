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

            if (!string.IsNullOrWhiteSpace(imagePath) &&
                TryCreateImageUri(imagePath, out var imageUri) &&
                imageUri is not null)
            {
                if (imageUri.IsFile)
                {
                    if (File.Exists(imageUri.LocalPath))
                    {
                        builder.AddInlineImage(imageUri);
                    }
                }
                else
                {
                    builder.AddInlineImage(imageUri);
                }
            }

            builder.Show(toast => toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(5));
        }
        catch
        {
            // Toast notifications are optional; swallow exceptions in environments that do not support them.
        }
    }

    private static bool TryCreateImageUri(string value, out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var created))
        {
            uri = created;
            return true;
        }

        if (Path.IsPathRooted(value))
        {
            uri = new Uri(value);
            return true;
        }

        uri = null;
        return false;
    }
}
