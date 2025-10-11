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
                    IncludeFamilyShared = false,
                    RequiredTags = new List<string> { "Indie" },
                    MinimumSizeOnDisk = 10,
                    MaximumSizeOnDisk = 20,
                },
                HistoryLimit = 10,
                RecentGameExclusionCount = 0,
            });

            var games = new[]
            {
                new GameEntry
                {
                    AppId = 1,
                    Title = "Eligible",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    SizeOnDisk = 15,
                    Tags = new[] { "Indie", "Roguelike" },
                },
                new GameEntry
                {
                    AppId = 2,
                    Title = "Not Installed",
                    InstallState = InstallState.Available,
                    OwnershipType = OwnershipType.Owned,
                    SizeOnDisk = 15,
                    Tags = new[] { "Indie" },
                },
                new GameEntry
                {
                    AppId = 3,
                    Title = "Family Shared",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.FamilyShared,
                    SizeOnDisk = 15,
                    Tags = new[] { "Indie" },
                },
                new GameEntry
                {
                    AppId = 4,
                    Title = "Missing Tag",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    SizeOnDisk = 15,
                    Tags = new[] { "Action" },
                },
                new GameEntry
                {
                    AppId = 5,
                    Title = "Too Large",
                    InstallState = InstallState.Installed,
                    OwnershipType = OwnershipType.Owned,
                    SizeOnDisk = 25,
                    Tags = new[] { "Indie" },
                },
            };

            var selected = engine.PickNext(games);

            selected.AppId.Should().Be(1u);
            engine.GetHistory().Should().ContainSingle(entry => entry.AppId == 1u);
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
            new GameEntry { AppId = 10, Title = "First", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned, Tags = new[] { "Strategy" } },
            new GameEntry { AppId = 20, Title = "Second", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned, Tags = new[] { "Puzzle" } },
            new GameEntry { AppId = 30, Title = "Third", InstallState = InstallState.Installed, OwnershipType = OwnershipType.Owned, Tags = new[] { "Adventure" } },
        };

        var preferencesFactory = () => new SelectionPreferences
        {
            Seed = 9876,
            HistoryLimit = 10,
            RecentGameExclusionCount = 0,
            Filters = new SelectionFilters
            {
                RequireInstalled = false,
                IncludeFamilyShared = true,
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

    private static IReadOnlyList<uint> RunSelectionSequence(IEnumerable<GameEntry> games, SelectionPreferences preferences, string settingsPath, int picks)
    {
        var engine = new SelectionEngine(settingsPath, () => DateTimeOffset.UnixEpoch);
        engine.UpdatePreferences(preferences);

        var results = new List<uint>();
        foreach (var _ in Enumerable.Range(0, picks))
        {
            results.Add(engine.PickNext(games).AppId);
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
