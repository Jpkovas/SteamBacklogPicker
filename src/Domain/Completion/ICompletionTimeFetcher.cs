using System;
using System.Threading;
using System.Threading.Tasks;

namespace Domain.Completion;

public interface ICompletionTimeFetcher
{
    Task<TimeSpan?> FetchEstimatedCompletionAsync(GameEntry game, CancellationToken cancellationToken);
}
