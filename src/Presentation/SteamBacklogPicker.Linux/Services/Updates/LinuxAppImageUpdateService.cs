using System;
using System.Diagnostics;
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

        if (string.Equals(Path.GetFullPath(marker.PendingBinaryPath), Path.GetFullPath(marker.TargetBinaryPath), StringComparison.Ordinal))
        {
            return;
        }

        await SchedulePostExitSwapAsync(marker, markerPath, cancellationToken);
    }

    private static async Task SchedulePostExitSwapAsync(PendingUpdateMarker marker, string markerPath, CancellationToken cancellationToken)
    {
        var stateDirectory = GetUpdateStateDirectory();
        Directory.CreateDirectory(stateDirectory);

        var currentProcessId = Environment.ProcessId;
        var scriptPath = Path.Combine(stateDirectory, "apply-pending-update.sh");
        var backupPath = marker.TargetBinaryPath + ".bak";
        var scriptContents = $"""
#!/usr/bin/env bash
set -eu

CURRENT_PID={currentProcessId}
TARGET_PATH='{EscapeForSingleQuotedShellLiteral(marker.TargetBinaryPath)}'
PENDING_PATH='{EscapeForSingleQuotedShellLiteral(marker.PendingBinaryPath)}'
BACKUP_PATH='{EscapeForSingleQuotedShellLiteral(backupPath)}'
MARKER_PATH='{EscapeForSingleQuotedShellLiteral(markerPath)}'
SCRIPT_PATH='{EscapeForSingleQuotedShellLiteral(scriptPath)}'

for _ in $(seq 1 300); do
  if ! kill -0 "$CURRENT_PID" 2>/dev/null; then
    break
  fi
  sleep 1
done

if [ ! -f "$PENDING_PATH" ]; then
  exit 0
fi

cp -f "$TARGET_PATH" "$BACKUP_PATH" || true
mv -f "$PENDING_PATH" "$TARGET_PATH"
chmod 755 "$TARGET_PATH"
rm -f "$MARKER_PATH"
rm -f "$BACKUP_PATH"
rm -f "$SCRIPT_PATH"
""";

        await File.WriteAllTextAsync(scriptPath, scriptContents, cancellationToken);
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Process.Start(new ProcessStartInfo
        {
            FileName = "/usr/bin/env",
            ArgumentList = { "bash", scriptPath },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

    }

    private static string EscapeForSingleQuotedShellLiteral(string value)
    {
        return value.Replace("'", "'\\''", StringComparison.Ordinal);
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
