using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using EpicDiscovery;

namespace SteamBacklogPicker.UI.Services.Library;

public sealed class EpicLibraryProvider : IGameLibraryProvider
{
    private readonly IEpicGameLibrary epicLibrary;

    public EpicLibraryProvider(IEpicGameLibrary epicLibrary)
    {
        this.epicLibrary = epicLibrary ?? throw new ArgumentNullException(nameof(epicLibrary));
    }

    public Storefront Storefront => Storefront.EpicGamesStore;

    public Task<IReadOnlyCollection<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        return epicLibrary.GetLibraryAsync(cancellationToken);
    }
}
