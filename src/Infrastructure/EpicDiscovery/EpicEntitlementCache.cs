using System.Collections.Concurrent;
using System.Text.Json;
using Domain;
using Microsoft.Extensions.Logging;

namespace EpicDiscovery;

public sealed class EpicEntitlementCache
{
    private readonly EpicGraphQlClient graphqlClient;
    private readonly ILogger<EpicEntitlementCache>? logger;
    private readonly string cachePath;
    private readonly ConcurrentDictionary<GameIdentifier, EpicEntitlement> entitlements = new();
    private volatile bool initialized;

    public EpicEntitlementCache(EpicGraphQlClient graphqlClient, ILogger<EpicEntitlementCache>? logger = null)
    {
        this.graphqlClient = graphqlClient ?? throw new ArgumentNullException(nameof(graphqlClient));
        this.logger = logger;
        cachePath = BuildCachePath();
    }

    public IReadOnlyCollection<EpicEntitlement> GetCachedEntitlements()
    {
        EnsureInitialized();
        return entitlements.Values
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<EpicEntitlement>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var results = await graphqlClient.GetEntitlementsAsync(cancellationToken).ConfigureAwait(false);
        if (results.Count == 0)
        {
            EnsureInitialized();
            return entitlements.Values.ToArray();
        }

        entitlements.Clear();
        foreach (var entitlement in results)
        {
            entitlements[entitlement.Id] = entitlement;
        }

        initialized = true;
        try
        {
            Persist();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to persist Epic entitlements cache");
        }

        return entitlements.Values.ToArray();
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        try
        {
            if (!File.Exists(cachePath))
            {
                initialized = true;
                return;
            }

            using var stream = File.OpenRead(cachePath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                initialized = true;
                return;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var entitlement = ParseEntitlement(element);
                if (entitlement is not null)
                {
                    entitlements[entitlement.Id] = entitlement;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to hydrate Epic entitlements cache from disk");
        }
        finally
        {
            initialized = true;
        }
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = entitlements.Values.ToArray();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        File.WriteAllText(cachePath, JsonSerializer.Serialize(payload, options));
    }

    private static EpicEntitlement? ParseEntitlement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        GameIdentifier? identifier = null;
        if (element.TryGetProperty("Id", out var idElement) && idElement.ValueKind == JsonValueKind.Object)
        {
            identifier = JsonSerializer.Deserialize<GameIdentifier>(idElement.GetRawText());
        }

        if (identifier is null)
        {
            return null;
        }

        var catalogItemId = TryGetString(element, "CatalogItemId");
        var catalogNamespace = TryGetString(element, "CatalogNamespace");
        var appName = TryGetString(element, "AppName");
        var title = TryGetString(element, "Title") ?? appName ?? catalogItemId ?? identifier.StoreSpecificId;

        return new EpicEntitlement
        {
            Id = identifier!,
            CatalogItemId = catalogItemId,
            CatalogNamespace = catalogNamespace,
            AppName = appName,
            Title = title ?? string.Empty,
        };
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        return null;
    }

    private static string BuildCachePath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamBacklogPicker", "Epic");
        return Path.Combine(folder, "entitlements.json");
    }
}
