using Domain.Selection;
using FluentAssertions;
using Xunit;

namespace Domain.Tests;

public sealed class SelectionPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SelectionPersistenceTests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SelectionPersistenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Load_ShouldSkipNullHistoryEntries()
    {
        File.WriteAllText(SettingsPath, """{"History":[null,{"AppId":10,"Title":"Valid"},null]}""");
        var engine = new SelectionEngine(SettingsPath);
        engine.GetHistory().Should().ContainSingle().Which.Id.Should().Be(GameIdentifier.ForSteam(10));
    }

    [Theory]
    [InlineData("{\"History\":[{\"Id\":null,\"Title\":\"Missing identity\"}]}", 0u)]
    [InlineData("{\"History\":[{\"Id\":null,\"AppId\":0}]}", 0u)]
    [InlineData("{\"History\":[{\"Id\":null,\"AppId\":10}]}", 10u)]
    public void Load_ShouldNormalizeNullHistoryIdentitiesWithoutPreventingStartup(string json, uint expectedAppId)
    {
        File.WriteAllText(SettingsPath, json);

        var engine = new SelectionEngine(SettingsPath);

        engine.GetHistory().Should().ContainSingle().Which.Id.Should().Be(
            expectedAppId == 0 ? GameIdentifier.Unknown : GameIdentifier.ForSteam(expectedAppId));
    }

    [Fact]
    public void FailedPreferencesSave_ShouldPreserveCommittedPreferences()
    {
        var engine = CreateEngine();
        var preferences = engine.GetPreferences();
        preferences.Filters.RequireInstalled = true;
        WithBlockedDestination(() =>
        {
            var update = () => engine.UpdatePreferences(preferences);
            update.Should().Throw<Exception>().Where(ex => ex is IOException || ex is UnauthorizedAccessException);
        });
        engine.GetPreferences().Filters.RequireInstalled.Should().BeFalse();
        new SelectionEngine(SettingsPath).GetPreferences().Filters.RequireInstalled.Should().BeFalse();
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void FailedDrawSave_ShouldPreserveHistoryAndSeededSequence()
    {
        var engine = CreateEngine();
        var games = Enumerable.Range(1, 10).Select(id => new GameEntry
        { Id = GameIdentifier.ForSteam((uint)id), Title = "Game " + id, ProductCategory = ProductCategory.Game }).ToArray();
        WithBlockedDestination(() =>
        {
            var draw = () => engine.PickNext(games);
            draw.Should().Throw<Exception>().Where(ex => ex is IOException || ex is UnauthorizedAccessException);
        });
        engine.GetHistory().Should().BeEmpty();
        engine.PickNext(games).SteamAppId.Should().Be((uint)(Math.Floor(new Random(3).NextDouble() * 10) + 1));
        engine.GetHistory().Should().ContainSingle();
        WithBlockedDestination(() =>
        {
            var clear = () => engine.ClearHistory();
            clear.Should().Throw<Exception>().Where(ex => ex is IOException || ex is UnauthorizedAccessException);
        });
        engine.GetHistory().Should().ContainSingle();
        new SelectionEngine(SettingsPath).GetHistory().Should().ContainSingle();
    }

    private SelectionEngine CreateEngine()
    {
        var engine = new SelectionEngine(SettingsPath);
        engine.UpdatePreferences(new SelectionPreferences { Seed = 3 });
        return engine;
    }
    private void WithBlockedDestination(Action action)
    {
        var saved = Path.Combine(_directory, "committed.json");
        File.Move(SettingsPath, saved);
        Directory.CreateDirectory(SettingsPath);
        try { action(); }
        finally { Directory.Delete(SettingsPath); File.Move(saved, SettingsPath); }
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
