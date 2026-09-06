using System;
using System.Collections.Concurrent;
using System.IO;
using Domain;
using SteamDiscovery;

namespace SteamBacklogPicker.UI.Services.GameArt;

public sealed class SteamGameArtLocator : IGameArtLocator
{
    private readonly ISteamLibraryLocator _libraryLocator;
    private readonly ConcurrentDictionary<uint, (DateTimeOffset CheckedAt, string? Path)> _resolved = new();

    public SteamGameArtLocator(ISteamLibraryLocator libraryLocator)
    {
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
    }

    public string? FindHeroImage(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return game.Id.Storefront switch
        {
            Storefront.Steam when game.SteamAppId is uint appId => FindCachedHeroImage(appId),
            _ => null,
        };
    }

    private string? FindCachedHeroImage(uint appId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_resolved.TryGetValue(appId, out var found) && now - found.CheckedAt < TimeSpan.FromMinutes(2))
            return found.Path;
        var path = FindSteamHeroImage(appId);
        if (_resolved.Count > 5000) _resolved.Clear();
        _resolved[appId] = (now, path);
        return path;
    }

    private string? FindSteamHeroImage(uint appId)
    {
        if (appId == 0)
        {
            return null;
        }

        var candidateFiles = new[]
        {
            // Old flat structure
            $"{appId}_library_hero.jpg",
            $"{appId}_capsule_616x353.jpg",
            $"{appId}_header.jpg",
            $"{appId}_library_600x900.jpg",

            // New subdirectory structure
            Path.Combine(appId.ToString(), "library_hero.jpg"),
            Path.Combine(appId.ToString(), "capsule_616x353.jpg"),
            Path.Combine(appId.ToString(), "header.jpg"),
            Path.Combine(appId.ToString(), "library_600x900.jpg")
        };

        foreach (var library in _libraryLocator.GetLibraryFolders())
        {
            if (string.IsNullOrWhiteSpace(library))
            {
                continue;
            }

            foreach (var candidate in candidateFiles)
            {
                var cachePath = Path.Combine(library, "appcache", "librarycache", candidate);
                if (File.Exists(cachePath))
                {
                    return cachePath;
                }
            }
        }

        return BuildCdnUri(appId);
    }

    private static string BuildCdnUri(uint appId)
        => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_hero.jpg";
}
