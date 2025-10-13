using System.Threading;
using System.Threading.Tasks;

namespace SteamBacklogPicker.UI.Services;

public interface IAppUpdateService
{
    Task CheckForUpdatesAsync(CancellationToken cancellationToken);
}
