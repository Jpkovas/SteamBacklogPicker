using System;
using System.Diagnostics;
using Domain;
using SteamBacklogPicker.UI.Services.Notifications;

namespace SteamBacklogPicker.Linux.Services.Notifications;

public sealed class LinuxToastNotificationService : IToastNotificationService
{
    public void ShowGameSelected(GameEntry game, string? imagePath)
    {
        ArgumentNullException.ThrowIfNull(game);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "notify-send",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(imagePath);
            }

            startInfo.ArgumentList.Add("Steam Backlog Picker");
            startInfo.ArgumentList.Add(game.Title);

            using var process = Process.Start(startInfo);
            process?.WaitForExit(1500);
        }
        catch
        {
            // Optional feature in environments without Freedesktop notification support.
        }
    }
}
