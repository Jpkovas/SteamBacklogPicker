using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using FluentAssertions;
using SteamBacklogPicker.Linux.Services.Updates;
using Xunit;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class LinuxAppImageUpdateServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _originalHome;
    private readonly string _originalAppImage;
    private readonly string _originalFeedUrl;
    private readonly string _originalSwapPid;
    private readonly string _originalEnableUnsignedFeed;
    private readonly string _originalPublicKey;

    public LinuxAppImageUpdateServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"sbp-linux-update-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _originalHome = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        _originalAppImage = Environment.GetEnvironmentVariable("APPIMAGE") ?? string.Empty;
        _originalFeedUrl = Environment.GetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL") ?? string.Empty;
        _originalSwapPid = Environment.GetEnvironmentVariable("SBP_LINUX_UPDATE_SWAP_PID") ?? string.Empty;
        _originalEnableUnsignedFeed = Environment.GetEnvironmentVariable("SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED") ?? string.Empty;
        _originalPublicKey = Environment.GetEnvironmentVariable("SBP_LINUX_UPDATE_PUBLIC_KEY") ?? string.Empty;

        Environment.SetEnvironmentVariable("HOME", _tempDirectory);
        Environment.SetEnvironmentVariable("SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED", "true");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldDownloadPendingBinaryAndMarker_WhenFeedHasNewerVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var appImagePath = Path.Combine(_tempDirectory, "SteamBacklogPicker.AppImage");
        await File.WriteAllTextAsync(appImagePath, "current-binary", Encoding.UTF8);
        Environment.SetEnvironmentVariable("APPIMAGE", appImagePath);

        const string updatePayload = "new-linux-binary";
        await using var server = CreateFeedServer(
            "SteamBacklogPicker.AppImage",
            updatePayload,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(updatePayload))));

        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", server.FeedUrl);

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        var markerPath = Path.Combine(updateDirectory, "pending-update.json");
        var pendingPath = Path.Combine(updateDirectory, "SteamBacklogPicker.pending");

        File.Exists(markerPath).Should().BeTrue();
        File.Exists(pendingPath).Should().BeTrue();
        (await File.ReadAllTextAsync(pendingPath, Encoding.UTF8)).Should().Be(updatePayload);
    }


    [Fact]
    public async Task CheckForUpdatesAsync_ShouldNotStagePendingUpdate_WhenUnsignedFeedOptInIsDisabledAndNoPublicKey()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var appImagePath = Path.Combine(_tempDirectory, "SteamBacklogPicker.AppImage");
        await File.WriteAllTextAsync(appImagePath, "current-binary", Encoding.UTF8);
        Environment.SetEnvironmentVariable("APPIMAGE", appImagePath);
        Environment.SetEnvironmentVariable("SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED", "false");

        const string updatePayload = "new-linux-binary";
        await using var server = CreateFeedServer(
            "SteamBacklogPicker.AppImage",
            updatePayload,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(updatePayload))));

        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", server.FeedUrl);

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        var markerPath = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates", "pending-update.json");
        File.Exists(markerPath).Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldDownloadPendingBinaryAndMarker_WhenSignedFeedHasNewerVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        using var signingKey = RSA.Create(2048);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_PUBLIC_KEY", signingKey.ExportSubjectPublicKeyInfoPem());
        Environment.SetEnvironmentVariable("SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED", "false");

        var appImagePath = Path.Combine(_tempDirectory, "SteamBacklogPicker.AppImage");
        await File.WriteAllTextAsync(appImagePath, "current-binary", Encoding.UTF8);
        Environment.SetEnvironmentVariable("APPIMAGE", appImagePath);

        const string updatePayload = "signed-linux-binary";
        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(updatePayload)));
        await using var server = CreateFeedServer(
            "SteamBacklogPicker.AppImage",
            updatePayload,
            sha256,
            (version, downloadUrl, hash) => SignFeedPayload(signingKey, version, downloadUrl, hash));

        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", server.FeedUrl);

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        var markerPath = Path.Combine(updateDirectory, "pending-update.json");
        var pendingPath = Path.Combine(updateDirectory, "SteamBacklogPicker.pending");

        File.Exists(markerPath).Should().BeTrue();
        File.Exists(pendingPath).Should().BeTrue();
        (await File.ReadAllTextAsync(pendingPath, Encoding.UTF8)).Should().Be(updatePayload);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldNotStagePendingUpdate_WhenSignedFeedSignatureDoesNotMatch()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        using var trustedKey = RSA.Create(2048);
        using var untrustedKey = RSA.Create(2048);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_PUBLIC_KEY", trustedKey.ExportSubjectPublicKeyInfoPem());
        Environment.SetEnvironmentVariable("SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED", "false");

        var appImagePath = Path.Combine(_tempDirectory, "SteamBacklogPicker.AppImage");
        await File.WriteAllTextAsync(appImagePath, "current-binary", Encoding.UTF8);
        Environment.SetEnvironmentVariable("APPIMAGE", appImagePath);

        const string updatePayload = "signed-linux-binary";
        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(updatePayload)));
        await using var server = CreateFeedServer(
            "SteamBacklogPicker.AppImage",
            updatePayload,
            sha256,
            (version, downloadUrl, hash) => SignFeedPayload(untrustedKey, version, downloadUrl, hash));

        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", server.FeedUrl);

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        File.Exists(Path.Combine(updateDirectory, "pending-update.json")).Should().BeFalse();
        File.Exists(Path.Combine(updateDirectory, "SteamBacklogPicker.pending")).Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldApplyPendingUpdate_WhenMarkerExists()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        Directory.CreateDirectory(updateDirectory);

        var targetPath = Path.Combine(_tempDirectory, "SteamBacklogPicker.AppImage");
        var pendingPath = Path.Combine(updateDirectory, "SteamBacklogPicker.pending");
        await File.WriteAllTextAsync(targetPath, "old-content", Encoding.UTF8);
        await File.WriteAllTextAsync(pendingPath, "new-content", Encoding.UTF8);

        var markerPath = Path.Combine(updateDirectory, "pending-update.json");
        var markerJson = JsonSerializer.Serialize(new
        {
            Version = "99.0.0.0",
            PendingBinaryPath = pendingPath,
            TargetBinaryPath = targetPath,
        });
        await File.WriteAllTextAsync(markerPath, markerJson, Encoding.UTF8);

        Environment.SetEnvironmentVariable("APPIMAGE", targetPath);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_SWAP_PID", "999999");
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", "http://127.0.0.1:9/unreachable");

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        await WaitForConditionAsync(() => !File.Exists(markerPath), TimeSpan.FromSeconds(10));
        (await File.ReadAllTextAsync(targetPath, Encoding.UTF8)).Should().Be("new-content");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldRestoreBackup_WhenPendingSwapFailsAfterBackup()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        Directory.CreateDirectory(updateDirectory);

        var targetPath = Path.Combine(_tempDirectory, "SteamBacklogPicker");
        var pendingPath = Path.Combine(updateDirectory, "SteamBacklogPicker.pending");
        await File.WriteAllTextAsync(targetPath, "old-content", Encoding.UTF8);
        Directory.CreateDirectory(pendingPath);

        var markerPath = Path.Combine(updateDirectory, "pending-update.json");
        var markerJson = JsonSerializer.Serialize(new
        {
            Version = "99.0.0.0",
            PendingBinaryPath = pendingPath,
            TargetBinaryPath = targetPath,
        });
        await File.WriteAllTextAsync(markerPath, markerJson, Encoding.UTF8);

        Environment.SetEnvironmentVariable("APPIMAGE", targetPath);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_SWAP_PID", "999999");
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", "http://127.0.0.1:9/unreachable");

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        await WaitForConditionAsync(() => !File.Exists(markerPath), TimeSpan.FromSeconds(10));
        (await File.ReadAllTextAsync(targetPath, Encoding.UTF8)).Should().Be("old-content");
        File.Exists(targetPath + ".bak").Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldNotStagePendingUpdate_WhenSha256IsMissing()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var appImagePath = Path.Combine(_tempDirectory, "SteamBacklogPicker.AppImage");
        await File.WriteAllTextAsync(appImagePath, "current-binary", Encoding.UTF8);
        Environment.SetEnvironmentVariable("APPIMAGE", appImagePath);

        await using var server = CreateFeedServer("SteamBacklogPicker.AppImage", "new-linux-binary", " ");

        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", server.FeedUrl);

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        var markerPath = Path.Combine(updateDirectory, "pending-update.json");
        var pendingPath = Path.Combine(updateDirectory, "SteamBacklogPicker.pending");

        File.Exists(markerPath).Should().BeFalse();
        File.Exists(pendingPath).Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldNotStagePendingUpdate_WhenFeedCannotBeDownloaded()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var appImagePath = Path.Combine(_tempDirectory, "SteamBacklogPicker");
        await File.WriteAllTextAsync(appImagePath, "current-binary", Encoding.UTF8);
        Environment.SetEnvironmentVariable("APPIMAGE", appImagePath);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", "http://127.0.0.1:9/linux-appimage-update.json");

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        File.Exists(Path.Combine(updateDirectory, "pending-update.json")).Should().BeFalse();
        File.Exists(Path.Combine(updateDirectory, "SteamBacklogPicker.pending")).Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldDeletePendingBinary_WhenSha256DoesNotMatch()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var appImagePath = Path.Combine(_tempDirectory, "SteamBacklogPicker");
        await File.WriteAllTextAsync(appImagePath, "current-binary", Encoding.UTF8);
        Environment.SetEnvironmentVariable("APPIMAGE", appImagePath);

        await using var server = CreateFeedServer("SteamBacklogPicker", "new-linux-binary", new string('0', 64));

        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", server.FeedUrl);

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        File.Exists(Path.Combine(updateDirectory, "pending-update.json")).Should().BeFalse();
        File.Exists(Path.Combine(updateDirectory, "SteamBacklogPicker.pending")).Should().BeFalse();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", string.IsNullOrEmpty(_originalHome) ? null : _originalHome);
        Environment.SetEnvironmentVariable("APPIMAGE", string.IsNullOrEmpty(_originalAppImage) ? null : _originalAppImage);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", string.IsNullOrEmpty(_originalFeedUrl) ? null : _originalFeedUrl);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_SWAP_PID", string.IsNullOrEmpty(_originalSwapPid) ? null : _originalSwapPid);
        Environment.SetEnvironmentVariable("SBP_ENABLE_UNSIGNED_LINUX_UPDATE_FEED", string.IsNullOrEmpty(_originalEnableUnsignedFeed) ? null : _originalEnableUnsignedFeed);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_PUBLIC_KEY", string.IsNullOrEmpty(_originalPublicKey) ? null : _originalPublicKey);

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static LocalFeedServer CreateFeedServer(
        string downloadFileName,
        string payload,
        string sha256,
        Func<string, string, string, string?>? signatureFactory = null)
    {
        const string version = "99.0.0.0";
        var downloadPath = $"/download/{downloadFileName}";
        return new LocalFeedServer(async context =>
        {
            switch (context.Request.Url?.AbsolutePath)
            {
                case "/linux-appimage-update.json":
                    var downloadUrl = $"http://127.0.0.1:{context.Request.LocalEndPoint!.Port}{downloadPath}";
                    var feed = JsonSerializer.Serialize(new
                    {
                        version,
                        downloadUrl,
                        sha256,
                        signature = signatureFactory?.Invoke(version, downloadUrl, sha256),
                    });
                    await WriteUtf8Async(context.Response, feed);
                    break;
                default:
                    if (context.Request.Url?.AbsolutePath == downloadPath)
                    {
                        await WriteUtf8Async(context.Response, payload);
                        break;
                    }

                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    break;
            }
        });
    }

    private static string SignFeedPayload(RSA key, string version, string downloadUrl, string sha256)
    {
        var payload = string.Join('\n', version.Trim(), downloadUrl.Trim(), sha256.Trim().ToUpperInvariant());
        var signature = key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    private static async Task WriteUtf8Async(HttpListenerResponse response, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        response.StatusCode = 200;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Timed out waiting for expected condition.");
            }

            await Task.Delay(100);
        }
    }

    private sealed class LocalFeedServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Func<HttpListenerContext, Task> _handler;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public LocalFeedServer(Func<HttpListenerContext, Task> handler)
        {
            _handler = handler;
            _listener = new HttpListener();
            var port = GetFreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            FeedUrl = $"http://127.0.0.1:{port}/linux-appimage-update.json";
            _loop = Task.Run(ListenAsync);
        }

        public string FeedUrl { get; }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
            await _loop;
            _cts.Dispose();
        }

        private async Task ListenAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    await _handler(context);
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
            }
        }

        private static int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
