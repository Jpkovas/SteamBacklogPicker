using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SteamBacklogPicker.UI.Services.Updates;

namespace SteamBacklogPicker.Linux.Services.Updates;

public sealed class LinuxAppImageUpdateService : IAppUpdateService
{
    private const string FeedEnvironmentVariable = "SBP_LINUX_UPDATE_FEED_URL";
    private const string SwapProcessIdOverrideEnvironmentVariable = "SBP_LINUX_UPDATE_SWAP_PID";
    private const string EnableUnsignedFeedEnvironmentVariable = "SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED";
    private const string PublicKeyEnvironmentVariable = "SBP_LINUX_UPDATE_PUBLIC_KEY";
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

            if (await ApplyPendingUpdateAsync(cancellationToken))
            {
                return;
            }

            var currentExecutablePath = ResolveCurrentExecutablePath();
            if (currentExecutablePath is null)
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
            if (feed is null || string.IsNullOrWhiteSpace(feed.Version) || string.IsNullOrWhiteSpace(feed.DownloadUrl) || string.IsNullOrWhiteSpace(feed.Sha256))
            {
                return;
            }

            if (!IsTrustedFeed(feed))
            {
                return;
            }

            if (!Uri.TryCreate(feed.DownloadUrl, UriKind.Absolute, out var downloadUri) || !IsAllowedUpdateUri(downloadUri))
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

            var pendingBinaryPath = Path.Combine(stateDirectory, "SteamBacklogPicker.pending");
            var downloadPath = pendingBinaryPath + "." + Guid.NewGuid().ToString("N");
            try
            {
                await using (var destination = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await using (var stream = await HttpClient.GetStreamAsync(downloadUri, cancellationToken))
                {
                    await stream.CopyToAsync(destination, cancellationToken);
                }

                if (!IsValidSha256(feed.Sha256, downloadPath))
                {
                    return;
                }

                File.Move(downloadPath, pendingBinaryPath, overwrite: true);
                var marker = new PendingUpdateMarker(feed.Version.Trim(), pendingBinaryPath, currentExecutablePath, feed.Sha256, feed.DownloadUrl, feed.Signature);
                var markerPath = Path.Combine(stateDirectory, "pending-update.json");
                var markerTemporaryPath = markerPath + "." + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllTextAsync(markerTemporaryPath, JsonSerializer.Serialize(marker), cancellationToken);
                    File.Move(markerTemporaryPath, markerPath, overwrite: true);
                }
                finally { File.Delete(markerTemporaryPath); }
            }
            finally { File.Delete(downloadPath); }
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

    private static async Task<bool> ApplyPendingUpdateAsync(CancellationToken cancellationToken)
    {
        var stateDirectory = GetUpdateStateDirectory();
        var markerPath = Path.Combine(stateDirectory, "pending-update.json");
        if (!File.Exists(markerPath)) { return false; }
        PendingUpdateMarker? marker;
        try
        {
            if (new FileInfo(markerPath).Length > 16 * 1024 || IsSymbolicLink(markerPath))
            {
                File.Delete(markerPath);
                return false;
            }
            marker = JsonSerializer.Deserialize<PendingUpdateMarker>(await File.ReadAllTextAsync(markerPath, cancellationToken));
        }
        catch (JsonException)
        {
            File.Delete(markerPath);
            return false;
        }

        var currentPath = ResolveCurrentExecutablePath();
        var expectedPendingPath = Path.Combine(stateDirectory, "SteamBacklogPicker.pending");
        if (marker is null || currentPath is null ||
            !PathsEqual(marker.PendingBinaryPath, expectedPendingPath) ||
            !PathsEqual(marker.TargetBinaryPath, currentPath) ||
            !Version.TryParse(marker.Version, out var targetVersion) ||
            targetVersion <= typeof(LinuxAppImageUpdateService).Assembly.GetName().Version ||
            string.IsNullOrWhiteSpace(marker.Sha256) || string.IsNullOrWhiteSpace(marker.DownloadUrl) ||
            !Uri.TryCreate(marker.DownloadUrl, UriKind.Absolute, out var downloadUri) || !IsAllowedUpdateUri(downloadUri) ||
            !IsTrustedFeed(new AppImageUpdateFeed(marker.Version, marker.DownloadUrl, marker.Sha256, marker.Signature)) ||
            !File.Exists(expectedPendingPath) || !File.Exists(currentPath) ||
            IsSymbolicLink(expectedPendingPath) || IsSymbolicLink(currentPath) ||
            !IsValidSha256(marker.Sha256, expectedPendingPath))
        {
            // Old markers without authenticated hash metadata must be downloaded again.
            File.Delete(markerPath);
            return false;
        }

        await SchedulePostExitSwapAsync(marker with
        {
            PendingBinaryPath = Path.GetFullPath(expectedPendingPath),
            TargetBinaryPath = Path.GetFullPath(currentPath)
        }, markerPath, cancellationToken);
        return true;
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) { return false; }
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.Ordinal); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    private static bool IsSymbolicLink(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static async Task SchedulePostExitSwapAsync(PendingUpdateMarker marker, string markerPath, CancellationToken cancellationToken)
    {
        var stateDirectory = GetUpdateStateDirectory();
        Directory.CreateDirectory(stateDirectory);

        var currentProcessId = ResolveSwapProcessId();
        var scriptPath = Path.Combine(stateDirectory, "apply-pending-update.sh");
        var backupPath = marker.TargetBinaryPath + ".bak";
        if (File.Exists(scriptPath) && IsSymbolicLink(scriptPath)) { throw new IOException("Update script is a symbolic link."); }
        var scriptContents = $$"""
#!/usr/bin/env bash
set -eu

CURRENT_PID={{currentProcessId}}
TARGET_PATH='{{EscapeForSingleQuotedShellLiteral(marker.TargetBinaryPath)}}'
PENDING_PATH='{{EscapeForSingleQuotedShellLiteral(marker.PendingBinaryPath)}}'
BACKUP_PATH='{{EscapeForSingleQuotedShellLiteral(backupPath)}}'
MARKER_PATH='{{EscapeForSingleQuotedShellLiteral(markerPath)}}'
SCRIPT_PATH='{{EscapeForSingleQuotedShellLiteral(scriptPath)}}'
EXPECTED_SHA256='{{marker.Sha256!.ToLowerInvariant()}}'
REPLACEMENT_PATH="$TARGET_PATH.update-$$"
BACKUP_CREATED=0

rollback_on_failure() {
  status=$?
  if [ "$status" -ne 0 ]; then
    if [ "$BACKUP_CREATED" -eq 1 ] && [ -f "$BACKUP_PATH" ]; then
      cp -f "$BACKUP_PATH" "$TARGET_PATH" || true
    fi
    rm -f "$MARKER_PATH"
    rm -f "$BACKUP_PATH"
    rm -f "$SCRIPT_PATH"
    rm -f "$REPLACEMENT_PATH"
  fi
}

trap rollback_on_failure EXIT

for _ in $(seq 1 300); do
  if [ ! -d "/proc/$CURRENT_PID" ]; then
    break
  fi
  if ! kill -0 "$CURRENT_PID" 2>/dev/null; then
    break
  fi
  sleep 1
done

# Do not replace a process that remained alive for the entire wait budget.
if kill -0 "$CURRENT_PID" 2>/dev/null; then
  rm -f "$SCRIPT_PATH"
  exit 0
fi

[ -f "$PENDING_PATH" ] && [ ! -L "$PENDING_PATH" ]
[ -f "$TARGET_PATH" ] && [ ! -L "$TARGET_PATH" ]
[ ! -L "$BACKUP_PATH" ]
# Copy beside the executable for an atomic rename even across filesystems.
# Recheck the copied bytes immediately before applying; staging verification can be stale.
(umask 077; set -C; cat "$PENDING_PATH" > "$REPLACEMENT_PATH")
ACTUAL_SHA256=$(sha256sum < "$REPLACEMENT_PATH")
[ "${ACTUAL_SHA256%% *}" = "$EXPECTED_SHA256" ]
chmod 755 "$REPLACEMENT_PATH"
cp -f "$TARGET_PATH" "$BACKUP_PATH"
BACKUP_CREATED=1
mv -f "$REPLACEMENT_PATH" "$TARGET_PATH"
rm -f "$PENDING_PATH"
rm -f "$MARKER_PATH"
rm -f "$BACKUP_PATH"
rm -f "$SCRIPT_PATH"
""";

        await File.WriteAllTextAsync(scriptPath, NormalizeShellScriptLineEndings(scriptContents), cancellationToken);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

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

    private static string NormalizeShellScriptLineEndings(string scriptContents)
        => scriptContents.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string GetUpdateStateDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "SteamBacklogPicker", "updates");
    }

    private static string? ResolveCurrentExecutablePath()
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath) && File.Exists(appImagePath))
        {
            return appImagePath;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            return null;
        }

        var fileName = Path.GetFileName(processPath);
        return fileName.StartsWith("SteamBacklogPicker", StringComparison.Ordinal)
            ? processPath
            : null;
    }

    private static bool IsAllowedUpdateUri(Uri uri)
    {
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidSha256(string expectedHash, string filePath)
    {
        if (expectedHash.Length != 64 || !IsHexString(expectedHash))
        {
            return false;
        }

        using var stream = File.OpenRead(filePath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHexString(string value)
    {
        foreach (var character in value)
        {
            var isHex =
                character is >= '0' and <= '9' ||
                character is >= 'a' and <= 'f' ||
                character is >= 'A' and <= 'F';

            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUnsignedFeedOptInEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnableUnsignedFeedEnvironmentVariable);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrustedFeed(AppImageUpdateFeed feed)
    {
        var publicKeyPem = Environment.GetEnvironmentVariable(PublicKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return IsUnsignedFeedOptInEnabled();
        }

        if (string.IsNullOrWhiteSpace(feed.Signature) || string.IsNullOrWhiteSpace(feed.Sha256))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            var payload = GetFeedSignaturePayload(feed);
            return rsa.VerifyData(
                System.Text.Encoding.UTF8.GetBytes(payload),
                Convert.FromBase64String(feed.Signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    private static string GetFeedSignaturePayload(AppImageUpdateFeed feed)
        => string.Join('\n', feed.Version.Trim(), feed.DownloadUrl.Trim(), feed.Sha256!.Trim().ToUpperInvariant());

    private static int ResolveSwapProcessId()
    {
        var overrideValue = Environment.GetEnvironmentVariable(SwapProcessIdOverrideEnvironmentVariable);
        if (int.TryParse(overrideValue, out var processId) && processId > 0)
        {
            return processId;
        }

        return Environment.ProcessId;
    }

    private sealed record AppImageUpdateFeed(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("signature")] string? Signature = null);

    private sealed record PendingUpdateMarker(string Version, string PendingBinaryPath, string TargetBinaryPath,
        string? Sha256 = null, string? DownloadUrl = null, string? Signature = null);
}
