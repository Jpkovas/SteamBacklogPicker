using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain;

namespace SteamBacklogPicker.UI.Services;

public sealed class CombinedGameLibraryService : IGameLibraryService
{
    private readonly IReadOnlyList<IGameLibraryProvider> providers;

    public CombinedGameLibraryService(IEnumerable<IGameLibraryProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();
    }

    public async Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var combined = new Dictionary<GameIdentifier, GameEntry>();

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = await provider.GetLibraryAsync(cancellationToken).ConfigureAwait(false);
            if (entries is null)
            {
                continue;
            }

            foreach (var game in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (game is null || game.Id is null || game.Id == GameIdentifier.Unknown)
                {
                    continue;
                }

                if (combined.TryGetValue(game.Id, out var existing))
                {
                    combined[game.Id] = MergeEntries(existing, game);
                }
                else
                {
                    combined[game.Id] = game;
                }
            }
        }

        var ordered = combined.Values
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(game => game.Id, GameIdentifier.Comparer)
            .ToList();

        return ordered;
    }

    private static GameEntry MergeEntries(GameEntry current, GameEntry incoming)
    {
        var title = ChooseString(current.Title, incoming.Title);
        var ownership = incoming.OwnershipType != OwnershipType.Unknown ? incoming.OwnershipType : current.OwnershipType;
        var installState = PrioritizeInstallState(current.InstallState, incoming.InstallState);
        var category = incoming.ProductCategory != ProductCategory.Unknown ? incoming.ProductCategory : current.ProductCategory;
        var sizeOnDisk = incoming.SizeOnDisk ?? current.SizeOnDisk;
        var lastPlayed = incoming.LastPlayed ?? current.LastPlayed;
        var deck = incoming.DeckCompatibility != SteamDeckCompatibility.Unknown
            ? incoming.DeckCompatibility
            : current.DeckCompatibility;

        var tags = MergeSets(current.Tags, incoming.Tags, StringComparer.OrdinalIgnoreCase);
        var categories = MergeSets(current.StoreCategoryIds, incoming.StoreCategoryIds);

        return current with
        {
            Title = title,
            OwnershipType = ownership,
            InstallState = installState,
            ProductCategory = category,
            SizeOnDisk = sizeOnDisk,
            LastPlayed = lastPlayed,
            Tags = tags,
            StoreCategoryIds = categories,
            DeckCompatibility = deck,
        };
    }

    private static string ChooseString(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return right ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        return right.Length > left.Length ? right : left;
    }

    private static IReadOnlyCollection<T> MergeSets<T>(IReadOnlyCollection<T>? left, IReadOnlyCollection<T>? right, IEqualityComparer<T>? comparer = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        var result = new HashSet<T>(comparer);
        if (left is not null)
        {
            foreach (var value in left)
            {
                result.Add(value);
            }
        }

        if (right is not null)
        {
            foreach (var value in right)
            {
                result.Add(value);
            }
        }

        return result.ToArray();
    }

    private static InstallState PrioritizeInstallState(InstallState current, InstallState incoming)
    {
        var ranked = new[] { InstallState.Installed, InstallState.Shared, InstallState.Available, InstallState.Unknown };
        var currentRank = Array.IndexOf(ranked, current);
        var incomingRank = Array.IndexOf(ranked, incoming);
        if (incomingRank < 0)
        {
            return current;
        }

        if (currentRank < 0)
        {
            return incoming;
        }

        return incomingRank < currentRank ? incoming : current;
    }
}
