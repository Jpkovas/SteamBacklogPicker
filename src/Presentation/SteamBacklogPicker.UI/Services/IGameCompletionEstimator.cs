using System;
using System.Threading;
using System.Threading.Tasks;

namespace SteamBacklogPicker.UI.Services;

public interface IGameCompletionEstimator
{
    Task<TimeSpan?> GetEstimatedCompletionAsync(string title, CancellationToken cancellationToken);
}
