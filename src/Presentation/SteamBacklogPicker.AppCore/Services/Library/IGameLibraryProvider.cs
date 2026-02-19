using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain;

namespace SteamBacklogPicker.UI.Services.Library;

public interface IGameLibraryProvider
{
    Storefront Storefront { get; }

    Task<IReadOnlyCollection<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default);
}
