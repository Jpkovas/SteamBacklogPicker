using System;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using Domain.Completion;
using Domain.Selection;

namespace SteamBacklogPicker.UI.Services;

public sealed class GameUserDataService : IGameUserDataService
{
    private static readonly TimeSpan CompletionCacheDuration = TimeSpan.FromDays(30);
    private readonly ISelectionEngine _selectionEngine;
    private readonly ICompletionTimeFetcher? _completionTimeFetcher;
    private readonly Func<DateTimeOffset> _clock;

    public GameUserDataService(
        ISelectionEngine selectionEngine,
        ICompletionTimeFetcher? completionTimeFetcher,
        Func<DateTimeOffset>? clock = null)
    {
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
        _completionTimeFetcher = completionTimeFetcher;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<GameUserData> LoadAsync(GameEntry game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        var current = _selectionEngine.GetUserData(game.AppId);
        if (_completionTimeFetcher is null)
        {
            return current;
        }

        if (ShouldRefreshCompletion(current))
        {
            try
            {
                var estimate = await _completionTimeFetcher
                    .FetchEstimatedCompletionAsync(game, cancellationToken)
                    .ConfigureAwait(false);
                if (estimate.HasValue)
                {
                    current = current with
                    {
                        EstimatedCompletionTime = estimate,
                        EstimatedCompletionFetchedAt = _clock(),
                    };
                    _selectionEngine.UpdateUserData(game.AppId, current);
                }
                else if (!current.EstimatedCompletionFetchedAt.HasValue)
                {
                    current = current with { EstimatedCompletionFetchedAt = _clock() };
                    _selectionEngine.UpdateUserData(game.AppId, current);
                }
            }
            catch
            {
                // Ignore fetch errors to keep offline operation resilient.
            }
        }

        return current;
    }

    public Task<GameUserData> SaveAsync(uint appId, GameUserData userData, CancellationToken cancellationToken = default)
    {
        _selectionEngine.UpdateUserData(appId, userData);
        return Task.FromResult(_selectionEngine.GetUserData(appId));
    }

    private bool ShouldRefreshCompletion(GameUserData userData)
    {
        if (_completionTimeFetcher is null)
        {
            return false;
        }

        if (!userData.EstimatedCompletionTime.HasValue)
        {
            if (!userData.EstimatedCompletionFetchedAt.HasValue)
            {
                return true;
            }

            return _clock() - userData.EstimatedCompletionFetchedAt.Value > CompletionCacheDuration;
        }

        if (!userData.EstimatedCompletionFetchedAt.HasValue)
        {
            return true;
        }

        return _clock() - userData.EstimatedCompletionFetchedAt.Value > CompletionCacheDuration;
    }
}
