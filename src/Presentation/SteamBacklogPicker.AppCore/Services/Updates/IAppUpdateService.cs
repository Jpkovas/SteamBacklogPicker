using System.Threading;
using System.Threading.Tasks;

namespace SteamBacklogPicker.UI.Services.Updates;

public interface IAppUpdateService
{
    Task CheckForUpdatesAsync(CancellationToken cancellationToken);
}
