using System.Threading;
using System.Threading.Tasks;

namespace SteamBacklogPicker.UI.Services.Updates;

public sealed class NoOpAppUpdateService : IAppUpdateService
{
    public Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
