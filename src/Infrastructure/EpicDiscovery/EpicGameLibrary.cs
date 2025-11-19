using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain;

namespace EpicDiscovery;

public sealed class EpicGameLibrary : IEpicGameLibrary
{
    private readonly EpicManifestCache manifestCache;
    private readonly EpicMetadataCache metadataCache;
    private readonly EpicEntitlementCache entitlementCache;

    public EpicGameLibrary(
        EpicManifestCache manifestCache,
        EpicMetadataCache metadataCache,
        EpicEntitlementCache entitlementCache)
    {
        this.manifestCache = manifestCache ?? throw new ArgumentNullException(nameof(manifestCache));
        this.metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        this.entitlementCache = entitlementCache ?? throw new ArgumentNullException(nameof(entitlementCache));
    }

    public async Task<IReadOnlyCollection<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        manifestCache.Refresh();
        metadataCache.RefreshLocalCaches();
        var entitlements = await entitlementCache.RefreshAsync(cancellationToken).ConfigureAwait(false);

        var installed = manifestCache.GetInstalledGames();
        var results = new Dictionary<GameIdentifier, GameEntry>(installed.Count);

        foreach (var entry in installed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[entry.Id] = entry;
        }

        foreach (var entitlement in entitlements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await metadataCache.EnsureMetadataAsync(entitlement, cancellationToken).ConfigureAwait(false);
            if (results.TryGetValue(entitlement.Id, out var existing))
            {
                results[entitlement.Id] = MergeInstalledWithEntitlement(existing, entitlement, metadata);
            }
            else
            {
                results[entitlement.Id] = CreateOwnedEntry(entitlement, metadata);
            }
        }

        var catalogEntries = metadataCache.GetAllCatalogEntries();
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

        return ordered;
    }

    private static GameEntry MergeInstalledWithEntitlement(GameEntry installed, EpicEntitlement entitlement, EpicCatalogItem? metadata)
    {
        var title = !string.IsNullOrWhiteSpace(installed.Title)
            ? installed.Title
            : metadata?.Title ?? entitlement.Title;
        var size = installed.SizeOnDisk ?? metadata?.SizeOnDisk;
        var lastPlayed = installed.LastPlayed ?? metadata?.LastModified;
        var tags = installed.Tags
            .Concat(metadata?.Tags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return installed with
        {
            Title = title,
            SizeOnDisk = size,
            LastPlayed = lastPlayed,
            OwnershipType = OwnershipType.Owned,
            InstallState = installed.InstallState == InstallState.Unknown ? InstallState.Available : installed.InstallState,
            Tags = tags,
        };
    }

    private static GameEntry CreateOwnedEntry(EpicEntitlement entitlement, EpicCatalogItem? metadata)
    {
        return new GameEntry
        {
            Id = entitlement.Id,
            Title = metadata?.Title ?? entitlement.Title,
            OwnershipType = OwnershipType.Owned,
            InstallState = InstallState.Available,
            SizeOnDisk = metadata?.SizeOnDisk,
            LastPlayed = metadata?.LastModified,
            Tags = metadata?.Tags ?? Array.Empty<string>(),
        };
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
