using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SteamBacklogPicker.UI.Services.Updates;

namespace SteamBacklogPicker.Linux.Services.Updates;

public sealed class LinuxAppImageUpdateService : IAppUpdateService
{
    private const string FeedEnvironmentVariable = "SBP_LINUX_UPDATE_FEED_URL";
    private const string DefaultFeedUrl = "https://github.com/Jpkovas/SteamBacklogPicker/releases/latest/download/linux-appimage-update.json";
    private static readonly HttpClient HttpClient = new();

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return;
            }

            await ApplyPendingUpdateAsync(cancellationToken);

            var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
            if (string.IsNullOrWhiteSpace(appImagePath) || !File.Exists(appImagePath))
            {
                return;
            }

            var feedUrl = Environment.GetEnvironmentVariable(FeedEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                feedUrl = DefaultFeedUrl;
            }

            var feedJson = await HttpClient.GetStringAsync(feedUrl, cancellationToken);
            var feed = JsonSerializer.Deserialize<AppImageUpdateFeed>(feedJson);
            if (feed is null || string.IsNullOrWhiteSpace(feed.Version) || string.IsNullOrWhiteSpace(feed.DownloadUrl))
            {
                return;
            }

            var currentVersion = typeof(LinuxAppImageUpdateService).Assembly.GetName().Version;
            if (!Version.TryParse(feed.Version, out var targetVersion) || currentVersion is null || targetVersion <= currentVersion)
            {
                return;
            }

            var stateDirectory = GetUpdateStateDirectory();
            Directory.CreateDirectory(stateDirectory);

            var pendingBinaryPath = Path.Combine(stateDirectory, "SteamBacklogPicker.pending.AppImage");
            await using (var destination = File.Create(pendingBinaryPath))
            await using (var stream = await HttpClient.GetStreamAsync(feed.DownloadUrl, cancellationToken))
            {
                await stream.CopyToAsync(destination, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(feed.Sha256) && !IsValidSha256(feed.Sha256, pendingBinaryPath))
            {
                File.Delete(pendingBinaryPath);
                return;
            }

            var marker = new PendingUpdateMarker(targetVersion.ToString(), pendingBinaryPath, appImagePath);
            var markerPath = Path.Combine(stateDirectory, "pending-update.json");
            await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(marker), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Updates are optional on Linux and should not break the app lifecycle.
        }
    }

    private static async Task ApplyPendingUpdateAsync(CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(GetUpdateStateDirectory(), "pending-update.json");
        if (!File.Exists(markerPath))
        {
            return;
        }

        var marker = JsonSerializer.Deserialize<PendingUpdateMarker>(await File.ReadAllTextAsync(markerPath, cancellationToken));
        if (marker is null || string.IsNullOrWhiteSpace(marker.PendingBinaryPath) || string.IsNullOrWhiteSpace(marker.TargetBinaryPath))
        {
            return;
        }

        if (!File.Exists(marker.PendingBinaryPath) || !File.Exists(marker.TargetBinaryPath))
        {
            return;
        }

        var backupPath = marker.TargetBinaryPath + ".bak";
        File.Copy(marker.TargetBinaryPath, backupPath, overwrite: true);
        File.Copy(marker.PendingBinaryPath, marker.TargetBinaryPath, overwrite: true);
        File.SetUnixFileMode(marker.TargetBinaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Delete(marker.PendingBinaryPath);
        File.Delete(markerPath);

        try
        {
            File.Delete(backupPath);
        }
        catch
        {
        }
    }

    private static string GetUpdateStateDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "SteamBacklogPicker", "updates");
    }

    private static bool IsValidSha256(string expectedHash, string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AppImageUpdateFeed(string Version, string DownloadUrl, string? Sha256);

    private sealed record PendingUpdateMarker(string Version, string PendingBinaryPath, string TargetBinaryPath);
}
