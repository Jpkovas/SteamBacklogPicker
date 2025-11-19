using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EpicDiscovery;

public sealed class EpicGraphQlClient
{
    private static readonly Uri GraphqlEndpoint = new("https://graphql.epicgames.com/graphql");

    private readonly HttpClient httpClient;
    private readonly EpicAuthenticationClient authenticationClient;
    private readonly ILogger<EpicGraphQlClient>? logger;

    public EpicGraphQlClient(HttpClient httpClient, EpicAuthenticationClient authenticationClient, ILogger<EpicGraphQlClient>? logger = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.authenticationClient = authenticationClient ?? throw new ArgumentNullException(nameof(authenticationClient));
        this.logger = logger;
    }

    public async Task<IReadOnlyCollection<EpicEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken = default)
    {
        var token = await authenticationClient.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return Array.Empty<EpicEntitlement>();
        }

        using var request = BuildLibraryRequest(token!);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger?.LogWarning("Epic GraphQL request failed with status {StatusCode}", response.StatusCode);
            return Array.Empty<EpicEntitlement>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseEntitlements(document.RootElement).ToArray();
    }

    private static HttpRequestMessage BuildLibraryRequest(string token)
    {
        var query = @"query LauncherQuery_GetLibraryItems {
  Launcher {
    libraryItems(params: {includeDlc: true, includeEarnedItems: true}) {
      namespace
      catalogItemId
      appName
      title
    }
  }
}";

        var payload = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["variables"] = new Dictionary<string, object?>(),
            ["operationName"] = "LauncherQuery_GetLibraryItems",
        };

        var request = new HttpRequestMessage(HttpMethod.Post, GraphqlEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static IEnumerable<EpicEntitlement> ParseEntitlements(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var dataElement))
        {
            yield break;
        }

        if (!dataElement.TryGetProperty("Launcher", out var launcherElement))
        {
            yield break;
        }

        if (!launcherElement.TryGetProperty("libraryItems", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var element in items.EnumerateArray())
        {
            var catalogNamespace = TryGetString(element, "namespace");
            var catalogItemId = TryGetString(element, "catalogItemId");
            var appName = TryGetString(element, "appName");
            var title = TryGetString(element, "title") ?? appName ?? catalogItemId ?? "Unknown Epic Game";
            var id = EpicIdentifierFactory.Create(catalogItemId, catalogNamespace, appName);

            yield return new EpicEntitlement
            {
                Id = id,
                CatalogItemId = catalogItemId,
                CatalogNamespace = catalogNamespace,
                AppName = appName,
                Title = title,
            };
        }
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }
}
