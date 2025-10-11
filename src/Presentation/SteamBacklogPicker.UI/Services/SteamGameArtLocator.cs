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

        var fileName = $"{appId}_library_600x900.jpg";
        foreach (var library in _libraryLocator.GetLibraryFolders())
        {
            if (string.IsNullOrWhiteSpace(library))
            {
                continue;
            }

            var cachePath = Path.Combine(library, "appcache", "librarycache", fileName);
            if (File.Exists(cachePath))
            {
                return cachePath;
            }
        }

        return null;
    }
}
