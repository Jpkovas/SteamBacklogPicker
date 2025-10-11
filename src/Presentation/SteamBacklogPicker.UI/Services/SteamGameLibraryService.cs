using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using SteamDiscovery;
using System.Linq;

namespace SteamBacklogPicker.UI.Services;

public sealed class SteamGameLibraryService : IGameLibraryService
{
    private readonly SteamAppManifestCache _cache;
    private readonly ISteamLibraryLocator _libraryLocator;

    public SteamGameLibraryService(SteamAppManifestCache cache, ISteamLibraryLocator libraryLocator)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
    }

    public Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _libraryLocator.Refresh();
        _cache.Refresh();
        var games = _cache.GetInstalledGames();
        return Task.FromResult<IReadOnlyList<GameEntry>>(games.ToArray());
    }
}
