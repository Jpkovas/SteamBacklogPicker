using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EpicDiscovery;

public sealed class EpicMetadataFetcher
{
    private static readonly Uri CatalogContentRoot = new("https://store-content.ak.epicgames.com/api/en-US/content/products/");
    private static readonly Uri GraphqlEndpoint = new("https://graphql.epicgames.com/graphql");

    private readonly HttpClient httpClient;
    private readonly EpicHeroArtCache heroArtCache;
    private readonly ILogger<EpicMetadataFetcher>? logger;

    public EpicMetadataFetcher(HttpClient httpClient, EpicHeroArtCache heroArtCache, ILogger<EpicMetadataFetcher>? logger = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.heroArtCache = heroArtCache ?? throw new ArgumentNullException(nameof(heroArtCache));
        this.logger = logger;
    }

    public async Task<EpicCatalogItem?> FetchAsync(EpicEntitlement entitlement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entitlement);

        var attempts = 0;
        while (attempts < 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var offer = await QueryCatalogOfferAsync(entitlement, cancellationToken).ConfigureAwait(false);
                if (offer is not null)
                {
                    return await PopulateHeroArtAsync(offer, cancellationToken).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(entitlement.AppName))
                {
                    var fromContent = await FetchFromContentServiceAsync(entitlement.AppName!, entitlement, cancellationToken).ConfigureAwait(false);
                    if (fromContent is not null)
                    {
                        return await PopulateHeroArtAsync(fromContent, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                if (attempts < 2)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempts));
                    logger?.LogWarning(ex, "Epic metadata fetch failed (attempt {Attempt}); retrying in {Delay}s", attempts + 1, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    logger?.LogWarning(ex, "Epic metadata fetch failed after {Attempt} attempts; skipping remote metadata", attempts + 1);
                    return null;
                }
            }

            attempts++;
        }

        return null;
    }

    private async Task<EpicCatalogItem?> QueryCatalogOfferAsync(EpicEntitlement entitlement, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entitlement.CatalogItemId) || string.IsNullOrWhiteSpace(entitlement.CatalogNamespace))
        {
            return null;
        }

        var query = @"query catalogOffers($namespace: String!, $id: String!) {
  Catalog {
    catalogOffer(namespace: $namespace, id: $id) {
      id
      namespace
      title
      productSlug
      keyImages { type url }
    }
  }
}";

        var payload = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["operationName"] = "catalogOffers",
            ["variables"] = new Dictionary<string, string>
            {
                ["namespace"] = entitlement.CatalogNamespace!,
                ["id"] = entitlement.CatalogItemId!,
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, GraphqlEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger?.LogDebug("Epic catalog offer query failed with status {Status}", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("Catalog", out var catalog))
        {
            return null;
        }

        if (!catalog.TryGetProperty("catalogOffer", out var offer) || offer.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ParseCatalogItem(entitlement, offer);
    }

    private async Task<EpicCatalogItem?> FetchFromContentServiceAsync(string slugCandidate, EpicEntitlement entitlement, CancellationToken cancellationToken)
    {
        var candidateSlug = slugCandidate.Trim('/');
        var requestUri = new Uri(CatalogContentRoot, candidateSlug);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var title = root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
            ? titleProp.GetString()
            : entitlement.Title;

        IReadOnlyCollection<EpicKeyImage> images = Array.Empty<EpicKeyImage>();
        if (root.TryGetProperty("keyImages", out var keyImages) && keyImages.ValueKind == JsonValueKind.Array)
        {
            images = keyImages.EnumerateArray()
                .Select(ParseKeyImage)
                .Where(image => image is not null)
                .Select(image => image!)
                .ToArray();
        }

        return new EpicCatalogItem
        {
            Id = entitlement.Id,
            CatalogItemId = entitlement.CatalogItemId,
            CatalogNamespace = entitlement.CatalogNamespace,
            AppName = entitlement.AppName,
            Title = title ?? entitlement.Title,
            ProductSlug = candidateSlug,
            KeyImages = images,
        };
    }

    private static EpicCatalogItem? ParseCatalogItem(EpicEntitlement entitlement, JsonElement offer)
    {
        var title = offer.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
            ? titleProp.GetString()
            : entitlement.Title;

        var productSlug = offer.TryGetProperty("productSlug", out var slugProp) && slugProp.ValueKind == JsonValueKind.String
            ? slugProp.GetString()
            : null;

        IReadOnlyCollection<EpicKeyImage> keyImages = Array.Empty<EpicKeyImage>();
        if (offer.TryGetProperty("keyImages", out var keyImagesElement) && keyImagesElement.ValueKind == JsonValueKind.Array)
        {
            keyImages = keyImagesElement.EnumerateArray()
                .Select(ParseKeyImage)
                .Where(image => image is not null)
                .Select(image => image!)
                .ToArray();
        }

        return new EpicCatalogItem
        {
            Id = entitlement.Id,
            CatalogItemId = entitlement.CatalogItemId,
            CatalogNamespace = entitlement.CatalogNamespace,
            AppName = entitlement.AppName,
            Title = title ?? entitlement.Title,
            ProductSlug = productSlug?.Trim('/'),
            KeyImages = keyImages,
        };
    }

    private static EpicKeyImage? ParseKeyImage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var url = element.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
            ? urlProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var type = element.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString()
            : null;

        return new EpicKeyImage
        {
            Type = type ?? string.Empty,
            Uri = url,
        };
    }

    private async Task<EpicCatalogItem> PopulateHeroArtAsync(EpicCatalogItem item, CancellationToken cancellationToken)
    {
        if (item.KeyImages is null || item.KeyImages.Count == 0)
        {
            return item;
        }

        var cached = await heroArtCache.PopulateAsync(item.KeyImages, cancellationToken).ConfigureAwait(false);
        return ReferenceEquals(cached, item.KeyImages)
            ? item
            : item with { KeyImages = cached };
    }
}
