using System.Text.Json;
using Domain;
using Microsoft.Extensions.Logging;

namespace EpicDiscovery;

public sealed class EpicMetadataCache
{
    private readonly EpicCatalogCache catalogCache;
    private readonly EpicMetadataFetcher fetcher;
    private readonly ILogger<EpicMetadataCache>? logger;
    private readonly string cachePath;
    private readonly Dictionary<GameIdentifier, EpicCatalogItem> fetchedEntries = new();
    private bool initialized;

    public EpicMetadataCache(EpicCatalogCache catalogCache, EpicMetadataFetcher fetcher, ILogger<EpicMetadataCache>? logger = null)
    {
        this.catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
        this.fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        this.logger = logger;
        cachePath = BuildCachePath();
    }

    public EpicCatalogItem? GetCatalogEntry(GameIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        EnsureInitialized();
        return catalogCache.GetCatalogEntry(identifier) ?? (fetchedEntries.TryGetValue(identifier, out var item) ? item : null);
    }

    public IReadOnlyCollection<EpicCatalogItem> GetAllCatalogEntries()
    {
        EnsureInitialized();
        var cached = catalogCache.GetCatalogEntries();
        return cached
            .Concat(fetchedEntries.Values)
            .GroupBy(entry => entry.Id)
            .Select(group => group.First())
            .ToArray();
    }

    public void RefreshLocalCaches()
    {
        catalogCache.Refresh();
        EnsureInitialized();
    }

    public async Task<EpicCatalogItem?> EnsureMetadataAsync(EpicEntitlement entitlement, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var existing = GetCatalogEntry(entitlement.Id);
        if (existing is not null)
        {
            return existing;
        }

        var fetched = await fetcher.FetchAsync(entitlement, cancellationToken).ConfigureAwait(false);
        if (fetched is null)
        {
            return null;
        }

        fetchedEntries[fetched.Id] = fetched;
        try
        {
            Persist();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to persist Epic metadata cache entry for {Id}", fetched.Id);
        }

        return fetched;
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
                try
                {
                    var item = JsonSerializer.Deserialize<EpicCatalogItem>(element.GetRawText());
                    if (item is not null)
                    {
                        fetchedEntries[item.Id] = item;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Failed to deserialize cached Epic metadata entry");
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to read Epic metadata cache from disk");
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

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        File.WriteAllText(cachePath, JsonSerializer.Serialize(fetchedEntries.Values, options));
    }

    private static string BuildCachePath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamBacklogPicker", "Epic");
        return Path.Combine(folder, "remote-metadata.json");
    }
}
