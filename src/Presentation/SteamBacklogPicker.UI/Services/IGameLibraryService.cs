using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain;

namespace SteamBacklogPicker.UI.Services;

public interface IGameLibraryService
{
    Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default);
}
