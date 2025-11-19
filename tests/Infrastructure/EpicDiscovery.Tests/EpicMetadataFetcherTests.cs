using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Domain;
using EpicDiscovery;
using FluentAssertions;
using Xunit;

namespace EpicDiscovery.Tests;

public sealed class EpicMetadataFetcherTests
{
    [Fact]
    public async Task FetchAsync_ShouldReturnCatalogOfferFromGraphQl()
    {
        var entitlement = new EpicEntitlement
        {
            Id = new GameIdentifier { Storefront = Storefront.EpicGamesStore, StoreSpecificId = "fn:fngame" },
            CatalogNamespace = "fn",
            CatalogItemId = "fngame",
            AppName = "Fortnite",
            Title = "Fortnite",
        };

        const string responseJson = """
        {
          "data": {
            "Catalog": {
              "catalogOffer": {
                "id": "fngame",
                "namespace": "fn",
                "title": "Fortnite Deluxe",
                "productSlug": "/fortnite",
                "keyImages": [
                  { "type": "DieselGameBox", "url": "https://cdn.epicgames.com/fn/diesel.jpg" }
                ]
              }
            }
          }
        }
        """;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                request.RequestUri!.Should().Be(new Uri("https://graphql.epicgames.com/graphql"));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
                };
                return Task.FromResult(response);
            }

            request.RequestUri!.ToString().Should().Be("https://cdn.epicgames.com/fn/diesel.jpg");
            var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            };
            return Task.FromResult(imageResponse);
        });

        var cacheDirectory = CreateTemporaryCacheDirectory();
        try
        {
            var httpClient = new HttpClient(handler);
            var heroArtCache = new EpicHeroArtCache(httpClient, new TestFileAccessor(), cacheDirectory: cacheDirectory);
            var fetcher = new EpicMetadataFetcher(httpClient, heroArtCache);

            var catalogItem = await fetcher.FetchAsync(entitlement);

            catalogItem.Should().NotBeNull();
            catalogItem!.Title.Should().Be("Fortnite Deluxe");
            catalogItem.ProductSlug.Should().Be("fortnite");
            var image = catalogItem.KeyImages.Should().ContainSingle()
                .Which;
            image.Type.Should().Be("DieselGameBox");
            image.Uri.Should().EndWith("diesel.jpg");
            image.Path.Should().NotBeNullOrWhiteSpace();
            File.Exists(image.Path!).Should().BeTrue();
        }
        finally
        {
            CleanupDirectory(cacheDirectory);
        }
    }

    [Fact]
    public async Task FetchAsync_ShouldFallbackToContentServiceWhenCatalogOfferMissing()
    {
        var entitlement = new EpicEntitlement
        {
            Id = new GameIdentifier { Storefront = Storefront.EpicGamesStore, StoreSpecificId = "rocket:rlgame" },
            CatalogNamespace = "rocket",
            CatalogItemId = "rlgame",
            AppName = "rocket-league",
            Title = "Rocket League",
        };

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.Host.Contains("graphql"))
            {
                var failure = new HttpResponseMessage(HttpStatusCode.NotFound);
                return Task.FromResult(failure);
            }

            if (request.RequestUri!.ToString().Equals("https://store-content.ak.epicgames.com/api/en-US/content/products/rocket-league"))
            {
                var contentResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{ \"title\": \"Rocket League Content\", \"keyImages\": [{ \"type\": \"OfferImageWide\", \"url\": \"https://cdn.epicgames.com/rocket/wide.jpg\" }] }",
                        Encoding.UTF8,
                        "application/json"),
                };
                return Task.FromResult(contentResponse);
            }

            request.RequestUri!.ToString().Should().Be("https://cdn.epicgames.com/rocket/wide.jpg");
            var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 4, 5, 6 })
            };
            return Task.FromResult(imageResponse);
        });

        var cacheDirectory = CreateTemporaryCacheDirectory();
        try
        {
            var httpClient = new HttpClient(handler);
            var heroArtCache = new EpicHeroArtCache(httpClient, new TestFileAccessor(), cacheDirectory: cacheDirectory);
            var fetcher = new EpicMetadataFetcher(httpClient, heroArtCache);

            var catalogItem = await fetcher.FetchAsync(entitlement);

            catalogItem.Should().NotBeNull();
            catalogItem!.Title.Should().Be("Rocket League Content");
            catalogItem.ProductSlug.Should().Be("rocket-league");
            var image = catalogItem.KeyImages.Should().ContainSingle()
                .Which;
            image.Type.Should().Be("OfferImageWide");
            image.Path.Should().NotBeNullOrWhiteSpace();
            handler.Requests.Should().HaveCount(3);
        }
        finally
        {
            CleanupDirectory(cacheDirectory);
        }
    }

    private static string CreateTemporaryCacheDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "EpicMetadataFetcherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}
