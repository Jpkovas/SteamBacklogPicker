using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Domain;
using EpicDiscovery;
using FluentAssertions;
using Xunit;

namespace EpicDiscovery.Tests;

public sealed class EpicGraphQlClientTests
{
    [Fact]
    public async Task GetEntitlementsAsync_ShouldSendGraphQlPayloadAndParseResponse()
    {
        const string responseJson = """
        {
          "data": {
            "Launcher": {
              "libraryItems": [
                {
                  "namespace": "fn",
                  "catalogItemId": "fngame",
                  "appName": "Fortnite",
                  "title": "Fortnite"
                },
                {
                  "namespace": "rocket",
                  "catalogItemId": "rlgame",
                  "appName": "RocketLeague",
                  "title": "Rocket League"
                }
              ]
            }
          }
        }
        """;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        });

        var httpClient = new HttpClient(handler);
        var authenticationClient = CreateAuthenticatedClient("eg1~access-token");
        var client = new EpicGraphQlClient(httpClient, authenticationClient);

        var entitlements = await client.GetEntitlementsAsync();

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://graphql.epicgames.com/graphql"));
        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Parameter.Should().Be("eg1~access-token");

        var payload = await request.Content!.ReadAsStringAsync();
        payload.Should().Contain("LauncherQuery_GetLibraryItems");
        payload.Should().Contain("\"includeDlc\": true");
        payload.Should().Contain("\"operationName\":\"LauncherQuery_GetLibraryItems\"");

        entitlements.Should().HaveCount(2);
        entitlements.Should().Contain(e =>
            e.Id.Storefront == Storefront.EpicGamesStore &&
            e.CatalogNamespace == "fn" &&
            e.CatalogItemId == "fngame" &&
            e.AppName == "Fortnite");
        entitlements.Should().Contain(e => e.Title == "Rocket League" && e.Id.StoreSpecificId == "rocket:rlgame");
    }

    private static EpicAuthenticationClient CreateAuthenticatedClient(string accessToken)
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("Token exchange should not occur"));
        var client = new EpicAuthenticationClient(new HttpClient(handler), new TestFileAccessor());
        var type = typeof(EpicAuthenticationClient);
        type.GetField("cachedToken", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, accessToken);
        type.GetField("cachedExpiration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, DateTimeOffset.UtcNow.AddHours(1));
        return client;
    }
}
