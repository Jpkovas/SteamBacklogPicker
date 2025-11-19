using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using Microsoft.Extensions.Logging;

namespace EpicDiscovery;

public sealed class EpicMetadataCache
{
    private readonly EpicCatalogCache catalogCache;
    private readonly EpicMetadataFetcher fetcher;
    private readonly EpicHeroArtCache heroArtCache;
    private readonly ILogger<EpicMetadataCache>? logger;
    private readonly string cachePath;
    private readonly Dictionary<GameIdentifier, EpicCatalogItem> fetchedEntries = new();
    private bool initialized;

    public EpicMetadataCache(
        EpicCatalogCache catalogCache,
        EpicMetadataFetcher fetcher,
        EpicHeroArtCache heroArtCache,
        ILogger<EpicMetadataCache>? logger = null)
    {
        this.catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
        this.fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        this.heroArtCache = heroArtCache ?? throw new ArgumentNullException(nameof(heroArtCache));
        this.logger = logger;
        cachePath = BuildCachePath();
    }

    public EpicCatalogItem? GetCatalogEntry(GameIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        EnsureInitialized();
        var item = catalogCache.GetCatalogEntry(identifier) ?? (fetchedEntries.TryGetValue(identifier, out var entry) ? entry : null);
        return AttachCachedImages(item);
    }

    public IReadOnlyCollection<EpicCatalogItem> GetAllCatalogEntries()
    {
        EnsureInitialized();
        var cached = catalogCache.GetCatalogEntries();
        return cached
            .Concat(fetchedEntries.Values)
            .GroupBy(entry => entry.Id)
            .Select(group => AttachCachedImages(group.First()))
            .Where(item => item is not null)
            .Select(item => item!)
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

        var hydrated = await PopulateHeroArtAsync(fetched, cancellationToken).ConfigureAwait(false);
        fetchedEntries[hydrated.Id] = hydrated;
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
                    var hydrated = AttachCachedImages(item);
                    if (hydrated is not null)
                    {
                        fetchedEntries[hydrated.Id] = hydrated;
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

    private EpicCatalogItem? AttachCachedImages(EpicCatalogItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var updated = heroArtCache.AttachCachedPaths(item.KeyImages);
        return ReferenceEquals(updated, item.KeyImages)
            ? item
            : item with { KeyImages = updated };
    }

    private async Task<EpicCatalogItem> PopulateHeroArtAsync(EpicCatalogItem item, CancellationToken cancellationToken)
    {
        var updated = await heroArtCache.PopulateAsync(item.KeyImages, cancellationToken).ConfigureAwait(false);
        return ReferenceEquals(updated, item.KeyImages)
            ? item
            : item with { KeyImages = updated };
    }
}
