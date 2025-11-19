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
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.Should().Be(new Uri("https://graphql.epicgames.com/graphql"));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        });

        var fetcher = new EpicMetadataFetcher(new HttpClient(handler));

        var catalogItem = await fetcher.FetchAsync(entitlement);

        catalogItem.Should().NotBeNull();
        catalogItem!.Title.Should().Be("Fortnite Deluxe");
        catalogItem.ProductSlug.Should().Be("fortnite");
        catalogItem.KeyImages.Should().ContainSingle(image => image.Type == "DieselGameBox" && image.Uri!.EndsWith("diesel.jpg"));
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

            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.ToString().Should().Be("https://store-content.ak.epicgames.com/api/en-US/content/products/rocket-league");
            var contentResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{ \"title\": \"Rocket League Content\", \"keyImages\": [{ \"type\": \"OfferImageWide\", \"url\": \"https://cdn.epicgames.com/rocket/wide.jpg\" }] }",
                    Encoding.UTF8,
                    "application/json"),
            };
            return Task.FromResult(contentResponse);
        });

        var fetcher = new EpicMetadataFetcher(new HttpClient(handler));

        var catalogItem = await fetcher.FetchAsync(entitlement);

        catalogItem.Should().NotBeNull();
        catalogItem!.Title.Should().Be("Rocket League Content");
        catalogItem.ProductSlug.Should().Be("rocket-league");
        catalogItem.KeyImages.Should().ContainSingle(image => image.Type == "OfferImageWide" && image.Uri == "https://cdn.epicgames.com/rocket/wide.jpg");
        handler.Requests.Should().HaveCount(2);
    }
}
