using System.Net;
using System.Text;
using FluentAssertions;
using SteamKit2;
using Xunit;

namespace SteamCatalog.Tests;

public sealed class MetadataSourceTests
{
    [Fact]
    public void ParseResponse_ShouldReadLocalizedNameTypePlatformsAndUseFixedArtworkOrigin()
    {
        var response = SteamStoreMetadataSource.ParseResponse(10, "brazilian", Encoding.UTF8.GetBytes("""
            {"10":{"success":true,"data":{"steam_appid":10,"name":"Jogo local","type":"game","platforms":{"windows":true,"mac":true,"linux":false},"header_image":"https://untrusted.invalid/tracker"}}}
            """));
        response.Status.Should().Be(CatalogLookupStatus.Found);
        response.Item!.Name.Should().Be("Jogo local");
        response.Item.Platforms.Should().Be(CatalogPlatforms.Windows | CatalogPlatforms.MacOS);
        response.Item.HeaderImageUrl.Should().Be("https://cdn.cloudflare.steamstatic.com/steam/apps/10/header.jpg");
    }

    [Theory]
    [InlineData("{\"10\":{\"success\":false}}", CatalogLookupStatus.NotFound)]
    [InlineData("{}", CatalogLookupStatus.Unavailable)]
    [InlineData("{\"10\":{\"success\":true,\"data\":{\"name\":\"Wrong\",\"steam_appid\":20}}}", CatalogLookupStatus.Unavailable)]
    [InlineData("not json", CatalogLookupStatus.Unavailable)]
    public void ParseResponse_ShouldDistinguishConfirmedMissingFromMalformedOrWrongIdentity(string json, CatalogLookupStatus expected)
    {
        SteamStoreMetadataSource.ParseResponse(10, "english", Encoding.UTF8.GetBytes(json)).Status.Should().Be(expected);
    }

    [Fact]
    public async Task FetchAsync_ShouldHonorRateLimitResponseWithoutConvertingItToNegativeCache()
    {
        using var client = new HttpClient(new Handler((request, _) =>
        {
            request.RequestUri!.Host.Should().Be("store.steampowered.com");
            request.RequestUri.Query.Should().Contain("appids=10").And.Contain("l=brazilian");
            request.Headers.Contains("Cookie").Should().BeFalse();
            request.Headers.Authorization.Should().BeNull();
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return Task.FromResult(response);
        }));
        using var source = new SteamStoreMetadataSource(client);

        var response = await source.FetchAsync(10, "brazilian", CancellationToken.None);

        response.Status.Should().Be(CatalogLookupStatus.Unavailable);
        response.Retryable.Should().BeTrue();
        response.RetryAfter.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FetchAsync_ShouldRejectOversizedResponseAndPropagateCancellation()
    {
        using var oversizedClient = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[2 * 1024 * 1024 + 1])
        })));
        using var oversized = new SteamStoreMetadataSource(oversizedClient);
        (await oversized.FetchAsync(10, "english", CancellationToken.None)).ErrorCode.Should().Be("store_response_too_large");

        using var cancellation = new CancellationTokenSource();
        using var blockedClient = new HttpClient(new Handler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var blocked = new SteamStoreMetadataSource(blockedClient);
        await FluentActions.Awaiting(() => blocked.FetchAsync(10, "english", cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void MapProductInfo_ShouldReadActualSteamKitKeyValueShape()
    {
        var root = new KeyValue("appinfo");
        var common = new KeyValue("common");
        root.Children.Add(common);
        common.Children.Add(new KeyValue("name", "English name"));
        common.Children.Add(new KeyValue("type", "Game"));
        common.Children.Add(new KeyValue("oslist", "windows,linux,macos"));
        var localized = new KeyValue("name_localized");
        localized.Children.Add(new KeyValue("brazilian", "Nome local"));
        common.Children.Add(localized);

        var response = SteamKitCatalogTransport.MapProductInfo(10, "brazilian", root);

        response.Item!.Name.Should().Be("Nome local");
        response.Item.ProductType.Should().Be("game");
        response.Item.Platforms.Should().Be(CatalogPlatforms.Windows | CatalogPlatforms.Linux | CatalogPlatforms.MacOS);
        response.Item.Source.Should().Be("steam-pics");
    }

    [Fact]
    public async Task FallbackSource_ShouldPreferPicsAndCircuitBreakUnavailablePrimary()
    {
        var time = new ManualTimeProvider();
        var primary = new DelegateSource((_, _, _) => Task.FromResult(new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true)));
        var fallback = new DelegateSource((id, language, _) => Task.FromResult(DelegateSource.Found(id, language)));
        var source = new FallbackCatalogMetadataSource(primary, fallback, time);
        await source.FetchAsync(10, "english", CancellationToken.None);
        await source.FetchAsync(20, "english", CancellationToken.None);
        primary.Calls.Should().Be(1);
        fallback.Calls.Should().Be(2);
        time.Advance(TimeSpan.FromMinutes(2));
        await source.FetchAsync(30, "english", CancellationToken.None);
        primary.Calls.Should().Be(2);

        var successful = new DelegateSource((id, language, _) => Task.FromResult(DelegateSource.Found(id, language)));
        await new FallbackCatalogMetadataSource(successful, fallback).FetchAsync(40, "english", CancellationToken.None);
        fallback.Calls.Should().Be(3);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
