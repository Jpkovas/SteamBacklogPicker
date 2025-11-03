using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain;

namespace EpicDiscovery;

public interface IEpicGameLibrary
{
    Task<IReadOnlyCollection<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default);
}
