using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain;

namespace EpicDiscovery;

public sealed class EpicGameLibrary : IEpicGameLibrary
{
    private readonly EpicManifestCache manifestCache;
    private readonly EpicCatalogCache catalogCache;

    public EpicGameLibrary(EpicManifestCache manifestCache, EpicCatalogCache catalogCache)
    {
        this.manifestCache = manifestCache ?? throw new ArgumentNullException(nameof(manifestCache));
        this.catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
    }

    public Task<IReadOnlyCollection<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        var installed = manifestCache.GetInstalledGames();
        var results = new Dictionary<GameIdentifier, GameEntry>(installed.Count);

        foreach (var entry in installed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[entry.Id] = entry;
        }

        var catalogEntries = catalogCache.GetCatalogEntries();
        foreach (var catalogEntry in catalogEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.TryGetValue(catalogEntry.Id, out var installedEntry))
            {
                results[catalogEntry.Id] = MergeEntries(installedEntry, catalogEntry);
            }
            else
            {
                results[catalogEntry.Id] = new GameEntry
                {
                    Id = catalogEntry.Id,
                    Title = catalogEntry.Title,
                    OwnershipType = OwnershipType.Owned,
                    InstallState = InstallState.Available,
                    SizeOnDisk = catalogEntry.SizeOnDisk,
                    LastPlayed = catalogEntry.LastModified,
                    Tags = catalogEntry.Tags,
                };
            }
        }

        var ordered = results.Values
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id.StoreSpecificId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<GameEntry>>(ordered);
    }

    private static GameEntry MergeEntries(GameEntry installed, EpicCatalogItem catalog)
    {
        var tags = installed.Tags
            .Concat(catalog.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var title = string.IsNullOrWhiteSpace(installed.Title) ? catalog.Title : installed.Title;
        var size = installed.SizeOnDisk ?? catalog.SizeOnDisk;
        var lastPlayed = installed.LastPlayed ?? catalog.LastModified;

        return installed with
        {
            Title = title,
            SizeOnDisk = size,
            LastPlayed = lastPlayed,
            Tags = tags,
        };
    }
}
