using System;
using System.IO;
using System.Net;
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

    public LinuxAppImageUpdateServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"sbp-linux-update-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _originalHome = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        _originalAppImage = Environment.GetEnvironmentVariable("APPIMAGE") ?? string.Empty;
        _originalFeedUrl = Environment.GetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL") ?? string.Empty;
        _originalSwapPid = Environment.GetEnvironmentVariable("SBP_LINUX_UPDATE_SWAP_PID") ?? string.Empty;

        Environment.SetEnvironmentVariable("HOME", _tempDirectory);
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
        await using var server = new LocalFeedServer(async context =>
        {
            switch (context.Request.Url?.AbsolutePath)
            {
                case "/linux-appimage-update.json":
                    var feed = JsonSerializer.Serialize(new
                    {
                        version = "99.0.0.0",
                        downloadUrl = $"http://127.0.0.1:{context.Request.LocalEndPoint!.Port}/download/SteamBacklogPicker.AppImage",
                        sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(updatePayload))),
                    });
                    await WriteUtf8Async(context.Response, feed);
                    break;
                case "/download/SteamBacklogPicker.AppImage":
                    await WriteUtf8Async(context.Response, updatePayload);
                    break;
                default:
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    break;
            }
        });

        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", server.FeedUrl);

        var sut = new LinuxAppImageUpdateService();
        await sut.CheckForUpdatesAsync(CancellationToken.None);

        var updateDirectory = Path.Combine(_tempDirectory, ".local", "share", "SteamBacklogPicker", "updates");
        var markerPath = Path.Combine(updateDirectory, "pending-update.json");
        var pendingPath = Path.Combine(updateDirectory, "SteamBacklogPicker.pending.AppImage");

        File.Exists(markerPath).Should().BeTrue();
        File.Exists(pendingPath).Should().BeTrue();
        (await File.ReadAllTextAsync(pendingPath, Encoding.UTF8)).Should().Be(updatePayload);
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
        var pendingPath = Path.Combine(updateDirectory, "SteamBacklogPicker.pending.AppImage");
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

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", string.IsNullOrEmpty(_originalHome) ? null : _originalHome);
        Environment.SetEnvironmentVariable("APPIMAGE", string.IsNullOrEmpty(_originalAppImage) ? null : _originalAppImage);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_FEED_URL", string.IsNullOrEmpty(_originalFeedUrl) ? null : _originalFeedUrl);
        Environment.SetEnvironmentVariable("SBP_LINUX_UPDATE_SWAP_PID", string.IsNullOrEmpty(_originalSwapPid) ? null : _originalSwapPid);

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
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
