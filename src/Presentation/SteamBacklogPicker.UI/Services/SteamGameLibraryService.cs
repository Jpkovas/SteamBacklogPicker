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

    public Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _libraryLocator.Refresh();
        _cache.Refresh();

        var installedGames = _cache.GetInstalledGames();
        var knownSteamApps = _fallback.GetKnownApps();
        var knownApps = knownSteamApps.ToDictionary(
            static pair => GameIdentifier.ForSteam(pair.Key),
            static pair => pair.Value);
        var collectionDefinitions = _fallback.GetCollections();

        var results = new Dictionary<GameIdentifier, GameEntry>();
        foreach (var game in installedGames)
        {
            var enriched = game;
            var id = game.Id;
            var steamAppId = game.SteamAppId;
            var isFamilyShared = steamAppId.HasValue && _fallback.IsSubscribedFromFamilySharing(steamAppId.Value);

            if (knownApps.TryGetValue(id, out var definition))
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

            results[id] = enriched;
        }

        foreach (var (id, definition) in knownApps)
        {
            if (results.ContainsKey(id))
            {
                continue;
            }

            var isFamilyShared = id.SteamAppId is uint steamAppId && _fallback.IsSubscribedFromFamilySharing(steamAppId);
            var ownership = isFamilyShared ? OwnershipType.FamilyShared : OwnershipType.Owned;
            var installState = ownership == OwnershipType.FamilyShared
                ? InstallState.Shared
                : (definition.IsInstalled ? InstallState.Installed : InstallState.Available);

            var title = !string.IsNullOrWhiteSpace(definition.Name)
                ? definition.Name!
                : $"App {id.StoreSpecificId}";
            var category = MapProductCategory(definition.Type);

            results[id] = new GameEntry
            {
                Id = id,
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
            foreach (var (id, names) in membership)
            {
                if (!results.TryGetValue(id, out var entry))
                {
                    continue;
                }

                var merged = (entry.Tags ?? Array.Empty<string>())
                    .Concat(names)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                results[id] = entry with { Tags = merged };
            }
        }

        var ordered = results.Values
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(game => game.Id, GameIdentifier.Comparer)
            .ToList();

        return Task.FromResult<IReadOnlyList<GameEntry>>(ordered);
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
    private static Dictionary<GameIdentifier, List<string>> BuildCollectionMembership(
        IReadOnlyList<SteamCollectionDefinition> definitions,
        Dictionary<GameIdentifier, GameEntry> entries)
    {
        var membership = new Dictionary<GameIdentifier, List<string>>();
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
                    var identifier = GameIdentifier.ForSteam(appId);
                    if (!entries.ContainsKey(identifier))
                    {
                        continue;
                    }

                    AddMembership(membership, identifier, definition.Name);
                }
            }

            if (definition.FilterSpec is null)
            {
                continue;
            }

            foreach (var (identifier, entry) in entries)
            {
                if (identifier.SteamAppId is not uint steamAppId)
                {
                    continue;
                }

                if (explicitSet is not null && explicitSet.Contains(steamAppId))
                {
                    continue;
                }

                if (MatchesCollection(entry, definition))
                {
                    AddMembership(membership, identifier, definition.Name);
                }
            }
        }

        return membership;
    }

    private static void AddMembership(Dictionary<GameIdentifier, List<string>> membership, GameIdentifier identifier, string collectionName)
    {
        if (!membership.TryGetValue(identifier, out var list))
        {
            list = new List<string>();
            membership[identifier] = list;
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
            3 => SupportsVr(entry),
            7 => SupportsSinglePlayer(entry),
            8 => SupportsMultiplayer(entry),
            13 => entry.DeckCompatibility is SteamDeckCompatibility.Verified or SteamDeckCompatibility.Playable,
            _ => false,
        };
    }

    private static bool SupportsSinglePlayer(GameEntry entry)
        => entry.StoreCategoryIds.Any(id => id == 2);

    private static bool SupportsMultiplayer(GameEntry entry)
        => entry.StoreCategoryIds.Any(id => id is 1 or 9 or 38 or 48 or 49);

    private static bool SupportsVr(GameEntry entry)
    {
        foreach (var category in entry.StoreCategoryIds)
        {
            switch (category)
            {
                case 31:
                case 32:
                case 33:
                case 34:
                case 35:
                case 36:
                case 37:
                case 38:
                case 39:
                case 52:
                case 53:
                case 54:
                    return true;
            }
        }

        return false;
    }

}


