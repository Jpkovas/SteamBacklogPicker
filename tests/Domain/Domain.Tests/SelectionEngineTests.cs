using System;
using System.Collections.Generic;
using System.Linq;
using Domain;
using Domain.Selection;
using FluentAssertions;
using Xunit;

namespace Domain.Tests;

public sealed class SelectionEngineTests
{
    [Fact]
    public void PickNext_ShouldRespectConfiguredFilters()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
            engine.UpdatePreferences(new SelectionPreferences
            {
                Filters = new SelectionFilters
                {
                    RequireInstalled = true,
                    IncludedCategories = new List<ProductCategory> { ProductCategory.Game },
                },
                HistoryLimit = 10,
                RecentGameExclusionCount = 0,
            });

            var games = new[]
            {
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(1),
                    Title = "Eligible",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    SizeOnDisk = 15,
                    ProductCategory = ProductCategory.Game,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(2),
                    Title = "Not Installed",
                    InstallState = InstallState.Available,
                    OwnershipType = OwnershipType.Owned,
                    SizeOnDisk = 15,
                    ProductCategory = ProductCategory.Game,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(3),
                    Title = "Different Category",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    SizeOnDisk = 25,
                    ProductCategory = ProductCategory.Soundtrack,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(4),
                    Title = "Family Shared Available",
                    InstallState = InstallState.Available,
                    OwnershipType = OwnershipType.FamilyShared,
                    SizeOnDisk = 10,
                    ProductCategory = ProductCategory.Game,
                },
            };

            var selected = engine.PickNext(games);

            selected.SteamAppId.Should().Be(1u);
            engine.GetHistory().Should().ContainSingle(entry => entry.Id == GameIdentifier.ForSteam(1));
        }
        finally
        {
            Cleanup(settingsPath);
        }
    }

    [Fact]
    public void PickNext_ShouldProduceDeterministicSequence_WhenSeedIsProvided()
    {
        var games = new[]
        {
            new GameEntry { Id = GameIdentifier.ForSteam(10), Title = "First", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned, Tags = new[] { "Strategy" } },
            new GameEntry { Id = GameIdentifier.ForSteam(20), Title = "Second", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned, Tags = new[] { "Puzzle" } },
            new GameEntry { Id = GameIdentifier.ForSteam(30), Title = "Third", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned, Tags = new[] { "Adventure" } },
        };

        var preferencesFactory = () => new SelectionPreferences
        {
            Seed = 9876,
            HistoryLimit = 10,
            RecentGameExclusionCount = 0,
            Filters = new SelectionFilters
            {
                RequireInstalled = false,
            },
        };

        var firstSettings = CreateSettingsPath();
        var secondSettings = CreateSettingsPath();

        try
        {
            var firstSequence = RunSelectionSequence(games, preferencesFactory(), firstSettings, picks: 5);
            var secondSequence = RunSelectionSequence(games, preferencesFactory(), secondSettings, picks: 5);

            secondSequence.Should().Equal(firstSequence);
        }
        finally
        {
            Cleanup(firstSettings);
            Cleanup(secondSettings);
        }
    }

    [Fact]
    public void PickNext_ShouldResumeSeededSequenceAcrossSessions()
    {
        var games = new[]
        {
            new GameEntry { Id = GameIdentifier.ForSteam(10), Title = "First", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned },
            new GameEntry { Id = GameIdentifier.ForSteam(20), Title = "Second", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned },
            new GameEntry { Id = GameIdentifier.ForSteam(30), Title = "Third", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned },
            new GameEntry { Id = GameIdentifier.ForSteam(40), Title = "Fourth", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned },
        };

        var preferences = new SelectionPreferences
        {
            Seed = 13579,
            HistoryLimit = 20,
            RecentGameExclusionCount = 0,
        };

        var settingsPath = CreateSettingsPath();

        try
        {
            var firstEngine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
            firstEngine.UpdatePreferences(preferences);

            const int firstBatchSize = 6;
            const int secondBatchSize = 4;

            var firstBatch = new List<uint>();
            for (var i = 0; i < firstBatchSize; i++)
            {
                firstBatch.Add(firstEngine.PickNext(games).SteamAppId!.Value);
            }

            var resumedEngine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);

            var resumedBatch = new List<uint>();
            for (var i = 0; i < secondBatchSize; i++)
            {
                resumedBatch.Add(resumedEngine.PickNext(games).SteamAppId!.Value);
            }

            var expectedSequence = ComputeUniformSequence(preferences.Seed!.Value, games, firstBatchSize + secondBatchSize);
            var actualSequence = firstBatch.Concat(resumedBatch).ToList();

            actualSequence.Should().Equal(expectedSequence);
        }
        finally
        {
            Cleanup(settingsPath);
        }
    }

    [Fact]
    public void FilterGames_ShouldRespectInstallationFilter()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
            engine.UpdatePreferences(new SelectionPreferences
            {
                Filters = new SelectionFilters
                {
                    RequireInstalled = true,
                },
                HistoryLimit = 10,
            });

            var games = new[]
            {
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(1),
                    Title = "Owned Installed",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(2),
                    Title = "Family Ownership",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.FamilyShared,
                    ProductCategory = ProductCategory.Game,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(3),
                    Title = "Shared InstallState",
                    InstallState = InstallState.Shared,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(4),
                    Title = "Available Only",
                    InstallState = InstallState.Available,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                },
            };

            var filtered = engine.FilterGames(games);

            filtered.Select(game => game.Id).Should().BeEquivalentTo(new[]
            {
                GameIdentifier.ForSteam(1),
                GameIdentifier.ForSteam(2),
                GameIdentifier.ForSteam(3),
            });
        }
        finally
        {
            Cleanup(settingsPath);
        }
    }

    [Fact]
    public void FilterGames_ShouldRespectCategorySelection()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
            engine.UpdatePreferences(new SelectionPreferences
            {
                Filters = new SelectionFilters
                {
                    RequireInstalled = false,
                    IncludedCategories = new List<ProductCategory>
                    {
                        ProductCategory.Game,
                        ProductCategory.Soundtrack,
                        ProductCategory.DLC,
                    },
                },
                HistoryLimit = 10,
            });

            var games = new[]
            {
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(1),
                    Title = "Base Game",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(2),
                    Title = "OST",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Soundtrack,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(3),
                    Title = "Design Tool",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Tool,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(4),
                    Title = "Expansion Pack",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.DLC,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(5),
                    Title = "Mystery Entry",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Unknown,
                },
            };

            var filtered = engine.FilterGames(games);

            filtered.Select(game => game.Id).Should().BeEquivalentTo(new[]
            {
                GameIdentifier.ForSteam(1),
                GameIdentifier.ForSteam(2),
                GameIdentifier.ForSteam(4),
                GameIdentifier.ForSteam(5),
            });
        }
        finally
        {
            Cleanup(settingsPath);
        }
    }

    [Fact]
    public void FilterGames_ShouldRespectCollectionSelection()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
            engine.UpdatePreferences(new SelectionPreferences
            {
                Filters = new SelectionFilters
                {
                    RequiredCollection = "Jogáveis no Deck",
                },
                HistoryLimit = 10,
            });

            var games = new[]
            {
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(1),
                    Title = "Deck Ready",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                    Tags = new[] { "Jogáveis no Deck", "Multijogador" },
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(2),
                    Title = "Only Multiplayer",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                    Tags = new[] { "Multijogador" },
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(3),
                    Title = "Deck Lowercase",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                    Tags = new[] { "jogáveis no deck" },
                },
            };

            var filtered = engine.FilterGames(games);

            filtered.Select(game => game.Id).Should().BeEquivalentTo(new[]
            {
                GameIdentifier.ForSteam(1),
                GameIdentifier.ForSteam(3),
            });
        }
        finally
        {
            Cleanup(settingsPath);
        }
    }

    [Fact]
    public void FilterGames_ShouldExcludeDeckUnsupportedGames_WhenPreferenceIsEnabled()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
            engine.UpdatePreferences(new SelectionPreferences
            {
                Filters = new SelectionFilters
                {
                    ExcludeDeckUnsupported = true,
                },
                HistoryLimit = 10,
            });

            var games = new[]
            {
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(1),
                    Title = "Verified",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                    DeckCompatibility = SteamDeckCompatibility.Verified,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(2),
                    Title = "Unsupported",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                    DeckCompatibility = SteamDeckCompatibility.Unsupported,
                },
                new GameEntry
                {
                    Id = GameIdentifier.ForSteam(3),
                    Title = "Playable",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    ProductCategory = ProductCategory.Game,
                    DeckCompatibility = SteamDeckCompatibility.Playable,
                },
            };

            var filtered = engine.FilterGames(games);

            filtered.Select(game => game.Id).Should().BeEquivalentTo(new[]
            {
                GameIdentifier.ForSteam(1),
                GameIdentifier.ForSteam(3),
            });
        }
        finally
        {
            Cleanup(settingsPath);
        }
    }

    [Fact]
    public void FilterGames_ShouldTreatStorefrontIdentifiersIndependently()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
            engine.UpdatePreferences(new SelectionPreferences
            {
                HistoryLimit = 5,
                RecentGameExclusionCount = 1,
            });

            var steamGame = new GameEntry
            {
                Id = GameIdentifier.ForSteam(42),
                Title = "Steam Original",
                InstallState = InstallState.Installed,
                OwnershipType = OwnershipType.Owned,
            };

            var gogGame = new GameEntry
            {
                Id = new GameIdentifier
                {
                    Storefront = Storefront.Gog,
                    StoreSpecificId = "42",
                },
                Title = "GOG Mirror",
                InstallState = InstallState.Installed,
                OwnershipType = OwnershipType.Owned,
            };

            _ = engine.PickNext(new[] { steamGame });

            var filtered = engine.FilterGames(new[] { steamGame, gogGame });

            filtered.Should().ContainSingle(game => game.Id == gogGame.Id);
        }
        finally
        {
            Cleanup(settingsPath);
        }
    }

    [Fact]
    public void FilterGames_ShouldRespectStorefrontFilters()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
            engine.UpdatePreferences(new SelectionPreferences
            {
                Filters = new SelectionFilters
                {
                    IncludedStorefronts = new List<Storefront> { Storefront.Steam },
                },
            });

            var steamGame = new GameEntry
            {
                Id = GameIdentifier.ForSteam(101),
                Title = "Steam",
                InstallState = InstallState.Installed,
                OwnershipType = OwnershipType.Owned,
            };

            var gogGame = new GameEntry
            {
                Id = new GameIdentifier
                {
                    Storefront = Storefront.Gog,
                    StoreSpecificId = "101",
                },
                Title = "GOG",
                InstallState = InstallState.Installed,
                OwnershipType = OwnershipType.Owned,
            };

            var filtered = engine.FilterGames(new[] { steamGame, gogGame });

            filtered.Should().ContainSingle(game => game.Id == steamGame.Id);
            filtered.Should().NotContain(game => game.Id == gogGame.Id);
        }
        finally
        {
            Cleanup(settingsPath);
        }
    }

    private static IReadOnlyList<uint> RunSelectionSequence(IEnumerable<GameEntry> games, SelectionPreferences preferences, string settingsPath, int picks)
    {
        var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
        engine.UpdatePreferences(preferences);

        var results = new List<uint>();
        foreach (var _ in Enumerable.Range(0, picks))
        {
            results.Add(engine.PickNext(games).SteamAppId!.Value);
        }

        return results;
    }

    private static IReadOnlyList<uint> ComputeUniformSequence(int seed, IReadOnlyList<GameEntry> games, int picks)
    {
        var random = new Random(seed);
        var results = new List<uint>(picks);

        for (var i = 0; i < picks; i++)
        {
            var threshold = random.NextDouble() * games.Count;
            var index = (int)Math.Floor(threshold);
            if (index >= games.Count)
            {
                index = games.Count - 1;
            }

            results.Add(games[index].SteamAppId!.Value);
        }

        return results;
    }

    private static string CreateSettingsPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SelectionEngineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }

    private static void Cleanup(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Ignored: best-effort cleanup.
            }
        }
    }
}
