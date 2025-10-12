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
        var knownApps = _fallback.GetKnownApps();

        var results = new Dictionary<uint, GameEntry>();
        foreach (var game in installedGames)
        {
            var enriched = game;

            if (knownApps.TryGetValue(game.AppId, out var definition))
            {
                if (!string.IsNullOrWhiteSpace(definition.Name) && !string.Equals(game.Title, definition.Name, StringComparison.Ordinal))
                {
                    enriched = enriched with { Title = definition.Name };
                }

                if (definition.Collections.Count > 0)
                {
                    enriched = enriched with { Tags = definition.Collections };
                }
            }

            results[game.AppId] = enriched;
        }

        foreach (var (appId, definition) in knownApps)
        {
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

            results[appId] = new GameEntry
            {
                AppId = appId,
                Title = title,
                OwnershipType = ownership,
                InstallState = installState,
                Tags = definition.Collections ?? Array.Empty<string>()
            };
        }

        var ordered = results.Values
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(game => game.AppId)
            .ToList();

        return Task.FromResult<IReadOnlyList<GameEntry>>(ordered);
    }
}
