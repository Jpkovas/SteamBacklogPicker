using System;
using System.IO;
using SteamDiscovery;

namespace SteamBacklogPicker.UI.Services;

public sealed class SteamGameArtLocator : IGameArtLocator
{
    private readonly ISteamLibraryLocator _libraryLocator;

    public SteamGameArtLocator(ISteamLibraryLocator libraryLocator)
    {
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
    }

    public string? FindHeroImage(uint appId)
    {
        if (appId == 0)
        {
            return null;
        }

        var candidateFiles = new[]
        {
            $"{appId}_header.jpg",
            $"{appId}_capsule_616x353.jpg",
            $"{appId}_library_hero.jpg",
            $"{appId}_library_600x900.jpg"
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
        => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
}
