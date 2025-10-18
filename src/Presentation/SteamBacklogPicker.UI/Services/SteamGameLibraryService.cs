using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using SteamDiscovery;
using System.Linq;
using SteamClientAdapter;

namespace SteamBacklogPicker.UI.Services;

public sealed class SteamGameLibraryService : IGameLibraryService
{
    private readonly SteamAppManifestCache _cache;
    private readonly ISteamLibraryLocator _libraryLocator;
    private readonly ISteamVdfFallback _fallback;

    public SteamGameLibraryService(
        SteamAppManifestCache cache,
        ISteamLibraryLocator libraryLocator,
        ISteamVdfFallback fallback)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public async Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ordered = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            _libraryLocator.Refresh();
            cancellationToken.ThrowIfCancellationRequested();

            var librariesChanged = _cache.Refresh();
            cancellationToken.ThrowIfCancellationRequested();

            if (librariesChanged)
            {
                _fallback.Invalidate();
            }

            var installedGames = _cache.GetInstalledGames();
            var knownApps = _fallback.GetKnownApps();
            var collectionDefinitions = _fallback.GetCollections();

            cancellationToken.ThrowIfCancellationRequested();

            var results = new Dictionary<uint, GameEntry>();
            foreach (var game in installedGames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var enriched = game;
                var isFamilyShared = _fallback.IsSubscribedFromFamilySharing(game.AppId);

                if (knownApps.TryGetValue(game.AppId, out var definition))
                {
                    if (!string.IsNullOrWhiteSpace(definition.Name) && !string.Equals(enriched.Title, definition.Name, StringComparison.Ordinal))
                    {
                        enriched = enriched with { Title = definition.Name };
                    }

                    if (definition.Collections.Count > 0)
                    {
                        var combinedTags = (enriched.Tags ?? Array.Empty<string>())
                            .Concat(definition.Collections)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        enriched = enriched with { Tags = combinedTags };
                    }

                    if (definition.StoreCategoryIds.Count > 0)
                    {
                        enriched = enriched with { StoreCategoryIds = definition.StoreCategoryIds };
                    }

                    if (definition.DeckCompatibility != SteamDeckCompatibility.Unknown)
                    {
                        enriched = enriched with { DeckCompatibility = definition.DeckCompatibility };
                    }

                    var category = MapProductCategory(definition.Type);
                    if (enriched.ProductCategory != category)
                    {
                        enriched = enriched with { ProductCategory = category };
                    }

                    var desiredOwnership = isFamilyShared ? OwnershipType.FamilyShared : OwnershipType.Owned;
                    if (enriched.OwnershipType != desiredOwnership)
                    {
                        enriched = enriched with { OwnershipType = desiredOwnership };
                    }

                    if (isFamilyShared)
                    {
                        if (enriched.InstallState != InstallState.Shared)
                        {
                            enriched = enriched with { InstallState = InstallState.Shared };
                        }
                    }
                    else if (definition.IsInstalled && enriched.InstallState != InstallState.Installed)
                    {
                        enriched = enriched with { InstallState = InstallState.Installed };
                    }
                }
                else if (isFamilyShared)
                {
                    if (enriched.OwnershipType != OwnershipType.FamilyShared || enriched.InstallState != InstallState.Shared)
                    {
                        enriched = enriched with
                        {
                            OwnershipType = OwnershipType.FamilyShared,
                            InstallState = InstallState.Shared
                        };
                    }
                }

                results[game.AppId] = enriched;
            }

            foreach (var (appId, definition) in knownApps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (results.ContainsKey(appId))
                {
                    continue;
                }

                var isFamilyShared = _fallback.IsSubscribedFromFamilySharing(appId);
                var ownership = isFamilyShared ? OwnershipType.FamilyShared : OwnershipType.Owned;
                var installState = ownership == OwnershipType.FamilyShared
                    ? InstallState.Shared
                    : (definition.IsInstalled ? InstallState.Installed : InstallState.Available);

                var title = !string.IsNullOrWhiteSpace(definition.Name) ? definition.Name! : $"App {appId}";
                var category = MapProductCategory(definition.Type);

                results[appId] = new GameEntry
                {
                    AppId = appId,
                    Title = title,
                    OwnershipType = ownership,
                    InstallState = installState,
                    ProductCategory = category,
                    Tags = definition.Collections ?? Array.Empty<string>(),
                    StoreCategoryIds = definition.StoreCategoryIds ?? Array.Empty<int>(),
                    DeckCompatibility = definition.DeckCompatibility
                };
            }

            if (collectionDefinitions.Count > 0)
            {
                var membership = BuildCollectionMembership(collectionDefinitions, results);
                foreach (var (appId, names) in membership)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!results.TryGetValue(appId, out var entry))
                    {
                        continue;
                    }

                    var merged = (entry.Tags ?? Array.Empty<string>())
                        .Concat(names)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    results[appId] = entry with { Tags = merged };
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            return (IReadOnlyList<GameEntry>)results.Values
                .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(game => game.AppId)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);

        return ordered;
    }

    private static ProductCategory MapProductCategory(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return ProductCategory.Game;
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "game" => ProductCategory.Game,
            "music" or "audio" or "soundtrack" => ProductCategory.Soundtrack,
            "application" or "software" => ProductCategory.Software,
            "tool" => ProductCategory.Tool,
            "video" or "movie" or "series" or "tv" or "episode" => ProductCategory.Video,
            "dlc" or "demo" or "mod" or "advertising" or "hardware" or "plugin" or "config" or "beta" => ProductCategory.Other,
            _ => ProductCategory.Other,
        };
    }
    private static Dictionary<uint, List<string>> BuildCollectionMembership(
        IReadOnlyList<SteamCollectionDefinition> definitions,
        Dictionary<uint, GameEntry> entries)
    {
        var membership = new Dictionary<uint, List<string>>();
        if (definitions.Count == 0 || entries.Count == 0)
        {
            return membership;
        }

        foreach (var definition in definitions)
        {
            HashSet<uint>? explicitSet = null;
            if (definition.ExplicitAppIds.Count > 0)
            {
                explicitSet = definition.ExplicitAppIds as HashSet<uint> ?? definition.ExplicitAppIds.ToHashSet();
                foreach (var appId in explicitSet)
                {
                    if (!entries.ContainsKey(appId))
                    {
                        continue;
                    }

                    AddMembership(membership, appId, definition.Name);
                }
            }

            if (definition.FilterSpec is null)
            {
                continue;
            }

            foreach (var (appId, entry) in entries)
            {
                if (explicitSet is not null && explicitSet.Contains(appId))
                {
                    continue;
                }

                if (MatchesCollection(entry, definition))
                {
                    AddMembership(membership, appId, definition.Name);
                }
            }
        }

        return membership;
    }

    private static void AddMembership(Dictionary<uint, List<string>> membership, uint appId, string collectionName)
    {
        if (!membership.TryGetValue(appId, out var list))
        {
            list = new List<string>();
            membership[appId] = list;
        }

        if (!list.Any(existing => string.Equals(existing, collectionName, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(collectionName);
        }
    }

    private static bool MatchesCollection(GameEntry entry, SteamCollectionDefinition definition)
    {
        if (definition.FilterSpec is null)
        {
            return false;
        }

        foreach (var group in definition.FilterSpec.Groups)
        {
            if (group.Options.Count == 0)
            {
                continue;
            }

            var groupMatch = group.AcceptUnion
                ? group.Options.Any(option => MatchesFilterOption(entry, option))
                : group.Options.All(option => MatchesFilterOption(entry, option));

            if (!groupMatch)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesFilterOption(GameEntry entry, int option)
    {
        return option switch
        {
            1 => entry.InstallState is InstallState.Installed or InstallState.Shared,
            3 => GameEntryCapabilities.SupportsVirtualReality(entry),
            7 => GameEntryCapabilities.SupportsSinglePlayer(entry),
            8 => GameEntryCapabilities.SupportsMultiplayer(entry),
            13 => entry.DeckCompatibility is SteamDeckCompatibility.Verified or SteamDeckCompatibility.Playable,
            _ => false,
        };
    }

}


