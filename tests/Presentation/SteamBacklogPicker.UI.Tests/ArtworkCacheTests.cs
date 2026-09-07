using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.GameArt;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class ArtworkCacheTests : IDisposable
{
    private const string Source = "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/10/header.jpg";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ArtworkCacheTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Cache_ShouldCoalesceConcurrentDownloadsAndServeOffline()
    {
        var handler = new Handler(() => Response(new byte[] { 10, 20, 30 }));
        using var client = new HttpClient(handler);
        var cache = new ArtworkCache(_directory, client);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync(Source, true)));
        handler.Calls.Should().Be(1);
        results.Should().OnlyContain(bytes => bytes != null && bytes.SequenceEqual(new byte[] { 10, 20, 30 }));
        (await new ArtworkCache(_directory, client).GetAsync(Source, false)).Should().Equal(10, 20, 30);
        handler.Calls.Should().Be(1);
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://example.org/image.jpg", true)]
    [InlineData("http://shared.fastly.steamstatic.com/image.jpg", true)]
    [InlineData(Source, false)]
    public async Task Cache_ShouldHonorHostAndNetworkBoundary(string source, bool network)
    {
        var handler = new Handler(() => Response(new byte[] { 1 }));
        using var client = new HttpClient(handler);
        (await new ArtworkCache(_directory, client).GetAsync(source, network)).Should().BeNull();
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("mime")]
    [InlineData("declared-size")]
    [InlineData("stream-size")]
    [InlineData("transport")]
    public async Task InvalidResponse_ShouldBackOffRepeatedRequests(string kind)
    {
        var handler = new Handler(() =>
        {
            if (kind == "transport") throw new HttpRequestException("fixture failure");
            var response = Response(new byte[] { 1 });
            if (kind == "status") response.StatusCode = HttpStatusCode.NotFound;
            if (kind == "mime") response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            if (kind == "declared-size") response.Content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
            if (kind == "stream-size")
            {
                response.Content = new StreamContent(new MemoryStream(new byte[8 * 1024 * 1024 + 1]));
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                response.Content.Headers.ContentLength = 1; // A lying length must not bypass the streaming bound.
            }
            return response;
        });
        using var client = new HttpClient(handler);
        var cache = new ArtworkCache(_directory, client);
        (await cache.GetAsync(Source, true)).Should().BeNull();
        (await cache.GetAsync(Source, true)).Should().BeNull();
        handler.Calls.Should().Be(1);
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task CanceledRequest_ShouldNotPoisonNextRequest()
    {
        var handler = new Handler(() => Response(new byte[] { 1 }));
        using var client = new HttpClient(handler);
        var cache = new ArtworkCache(_directory, client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = () => cache.GetAsync(Source, true, cancellation.Token);
        await request.Should().ThrowAsync<OperationCanceledException>();
        (await cache.GetAsync(Source, true)).Should().Equal(1);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task LocalFile_ShouldRejectOversizedAndLockedFilesWithoutNetwork()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "local.png");
        var handler = new Handler(() => Response(new byte[] { 1 }));
        using var client = new HttpClient(handler);
        var cache = new ArtworkCache(_directory, client);
        using (var file = File.Create(path)) file.SetLength(8 * 1024 * 1024 + 1);
        (await cache.GetAsync(path, true)).Should().BeNull();
        File.WriteAllBytes(path, new byte[] { 1, 2 });
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            (await cache.GetAsync(path, true)).Should().BeNull();
        (await cache.GetAsync(path, false)).Should().Equal(1, 2);
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DiskCache_ShouldRemainBoundedAcrossConcurrentWrites()
    {
        Directory.CreateDirectory(_directory);
        for (var i = 0; i < 300; i++) File.WriteAllBytes(Path.Combine(_directory, $"fixture-{i}.img"), new byte[] { 1 });
        var handler = new Handler(() => Response(new byte[] { 1, 2 }));
        using var client = new HttpClient(handler);
        var cache = new ArtworkCache(_directory, client);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => cache.GetAsync(Source + "?item=" + i, true)));
        Directory.GetFiles(_directory, "*.img").Should().HaveCount(300);
    }

    private static HttpResponseMessage Response(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref _calls); return Task.FromResult(response()); }
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
