using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class CombinedGameLibraryServiceTests
{
    [Fact]
    public async Task GetLibraryAsync_ShouldAggregateEntriesFromAllProviders()
    {
        var steamGame = new GameEntry
        {
            Id = GameIdentifier.ForSteam(1),
            Title = "Steam Game",
            InstallState = InstallState.Installed,
            OwnershipType = OwnershipType.Owned,
        };
        var epicGame = new GameEntry
        {
            Id = new GameIdentifier { Storefront = Storefront.EpicGamesStore, StoreSpecificId = "epic-1" },
            Title = "Epic Game",
            InstallState = InstallState.Available,
            OwnershipType = OwnershipType.Owned,
        };

        var gogGame = new GameEntry
        {
            Id = new GameIdentifier { Storefront = Storefront.Gog, StoreSpecificId = "epic-1" },
            Title = "GOG Port",
            InstallState = InstallState.Installed,
            OwnershipType = OwnershipType.Owned,
        };

        var service = new CombinedGameLibraryService(new[]
        {
            new FakeLibraryProvider(Storefront.Steam, steamGame),
            new FakeLibraryProvider(Storefront.EpicGamesStore, epicGame),
            new FakeLibraryProvider(Storefront.Gog, gogGame),
        });

        var results = await service.GetLibraryAsync();

        results.Should().Contain(new[] { steamGame, epicGame, gogGame });
        results.Should().Contain(entry => entry.Id.Storefront == Storefront.EpicGamesStore && entry.Id.StoreSpecificId == "epic-1");
        results.Should().Contain(entry => entry.Id.Storefront == Storefront.Gog && entry.Id.StoreSpecificId == "epic-1");
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldMergeDuplicateEntries()
    {
        var id = GameIdentifier.ForSteam(7);
        var installed = new GameEntry
        {
            Id = id,
            Title = "Base",
            InstallState = InstallState.Installed,
            OwnershipType = OwnershipType.Owned,
            Tags = new[] { "Installed" },
            StoreCategoryIds = new[] { 1 },
            DeckCompatibility = SteamDeckCompatibility.Unknown,
        };
        var enriched = new GameEntry
        {
            Id = id,
            Title = "Base Deluxe",
            InstallState = InstallState.Available,
            OwnershipType = OwnershipType.Owned,
            Tags = new[] { "Catalog" },
            StoreCategoryIds = new[] { 2 },
            DeckCompatibility = SteamDeckCompatibility.Verified,
        };

        var service = new CombinedGameLibraryService(new[]
        {
            new FakeLibraryProvider(Storefront.Steam, installed),
            new FakeLibraryProvider(Storefront.Steam, enriched),
        });

        var results = await service.GetLibraryAsync();
        var entry = results.Should().ContainSingle(game => game.Id == id).Subject;

        entry.Title.Should().Be("Base Deluxe");
        entry.InstallState.Should().Be(InstallState.Installed);
        entry.Tags.Should().Contain(new[] { "Installed", "Catalog" });
        entry.StoreCategoryIds.Should().Contain(new[] { 1, 2 });
        entry.DeckCompatibility.Should().Be(SteamDeckCompatibility.Verified);
    }

    private sealed class FakeLibraryProvider : IGameLibraryProvider
    {
        private readonly IReadOnlyCollection<GameEntry> entries;

        public FakeLibraryProvider(Storefront storefront, params GameEntry[] entries)
        {
            Storefront = storefront;
            this.entries = entries ?? Array.Empty<GameEntry>();
        }

        public Storefront Storefront { get; }

        public Task<IReadOnlyCollection<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entries);
        }
    }
}
