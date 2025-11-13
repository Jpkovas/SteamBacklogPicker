using System;
using System.IO;
using System.Linq;
using Domain;
using EpicDiscovery;
using EpicDiscovery.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace EpicDiscovery.Tests;

public sealed class EpicCatalogCacheTests : IDisposable
{
    private readonly string workingDirectory;

    public EpicCatalogCacheTests()
    {
        workingDirectory = Path.Combine(Path.GetTempPath(), "EpicCatalogCacheTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
    }

    [Fact]
    public void GetCatalogEntries_ShouldLoadFromJsonCaches()
    {
        var catalogDirectory = Path.Combine(workingDirectory, "json");
        Directory.CreateDirectory(catalogDirectory);
        File.Copy(GetFixturePath("Catalog/catalog_items.json"), Path.Combine(catalogDirectory, "catalog_items.json"));

        using var cache = new EpicCatalogCache(
            new FakeEpicLauncherLocator(catalogDirectories: new[] { catalogDirectory }),
            new TestFileAccessor());

        var entries = cache.GetCatalogEntries();

        entries.Should().HaveCount(2);
        entries.Should().Contain(entry =>
            entry.Id.Storefront == Storefront.EpicGamesStore &&
            entry.Id.StoreSpecificId == "fn:fngame" &&
            entry.Tags.Contains("action") &&
            entry.KeyImages.Any(image =>
                image.Type == "DieselGameBox" &&
                image.Uri == "https://cdn.epicgames.com/fn/fortnite_diesel.jpg" &&
                image.Path == "C:/Games/Epic/Fortnite/Images/diesel.jpg"));
        entries.Should().Contain(entry =>
            entry.Id.StoreSpecificId == "rocket:rlgame" &&
            entry.Tags.Contains("sports") &&
            entry.Tags.Contains("soccer") &&
            entry.KeyImages.Any(image =>
                image.Type == "OfferImageWide" &&
                image.Uri == "https://cdn.epicgames.com/rocket/rocketleague_wide.jpg"));
    }

    [Fact]
    public void GetCatalogEntries_ShouldLoadFromSqliteCaches()
    {
        var catalogDirectory = Path.Combine(workingDirectory, "sqlite");
        Directory.CreateDirectory(catalogDirectory);

        new SqliteCatalogFixtureBuilder()
            .AddCatalogItem(item => item
                .WithIdentifiers("fngame", "fn", "Fortnite")
                .WithTitle("Fortnite Deluxe")
                .AddTags("action", "battle-royale")
                .AddKeyImage("DieselGameBox", uri: "https://cdn.epicgames.com/fn/fortnite_diesel.jpg", path: "C:/Games/Epic/Fortnite/Images/diesel.jpg"))
            .AddCatalogItem(item => item
                .WithIdentifiers("rlgame", "rocket", "RocketLeague")
                .WithTitle("Rocket League")
                .AddTags("sports", "soccer")
                .AddKeyImage("OfferImageWide", uri: "https://cdn.epicgames.com/rocket/rocketleague_wide.jpg"))
            .Build(catalogDirectory);

        using var cache = new EpicCatalogCache(
            new FakeEpicLauncherLocator(catalogDirectories: new[] { catalogDirectory }),
            new TestFileAccessor());

        var entries = cache.GetCatalogEntries();

        entries.Should().HaveCount(2);
        entries.Should().Contain(entry =>
            entry.Id.StoreSpecificId == "fn:fngame" &&
            entry.Title == "Fortnite Deluxe" &&
            entry.Tags.Contains("battle-royale") &&
            entry.KeyImages.Any(image =>
                image.Type == "DieselGameBox" &&
                image.Uri == "https://cdn.epicgames.com/fn/fortnite_diesel.jpg"));
        entries.Should().Contain(entry =>
            entry.Id.StoreSpecificId == "rocket:rlgame" &&
            entry.Tags.Contains("sports") &&
            entry.KeyImages.Any(image =>
                image.Type == "OfferImageWide" &&
                image.Uri == "https://cdn.epicgames.com/rocket/rocketleague_wide.jpg"));
    }

    [Fact]
    public void GetCatalogEntries_ShouldParseNestedCatalogContainers()
    {
        var catalogDirectory = PrepareCatalogDirectory("Catalog/catalog_nested.json");

        using var cache = new EpicCatalogCache(
            new FakeEpicLauncherLocator(catalogDirectories: new[] { catalogDirectory }),
            new TestFileAccessor());

        var entries = cache.GetCatalogEntries();

        entries.Should().HaveCount(2);

        var deepGame = entries.Should().ContainSingle(entry => entry.Id.StoreSpecificId == "nested:deepgame").Subject;
        deepGame.SizeOnDisk.Should().Be(54321);
        deepGame.LastModified.Should().Be(DateTimeOffset.Parse("2024-05-01T12:00:00Z"));
        deepGame.Tags.Should().BeEquivalentTo(new[] { "adventure", "story", "narrative", "co-op" });

        var builderGame = entries.Should().ContainSingle(entry => entry.Id.StoreSpecificId == "nested:buildergame").Subject;
        builderGame.SizeOnDisk.Should().Be(7654321);
        builderGame.LastModified.Should().Be(DateTimeOffset.Parse("2024-04-10T09:45:00Z"));
        builderGame.Tags.Should().BeEquivalentTo(new[] { "simulation", "builder", "creative" });
    }

    [Fact]
    public void GetCatalogEntries_ShouldParseWrappedCatalogItems()
    {
        var catalogDirectory = PrepareCatalogDirectory("Catalog/catalog_wrapped.json");

        using var cache = new EpicCatalogCache(
            new FakeEpicLauncherLocator(catalogDirectories: new[] { catalogDirectory }),
            new TestFileAccessor());

        var entries = cache.GetCatalogEntries();

        entries.Should().HaveCount(2);

        var wrappedGame = entries.Should().ContainSingle(entry => entry.Id.StoreSpecificId == "wrapped:wrappedgame").Subject;
        wrappedGame.SizeOnDisk.Should().Be(24680);
        wrappedGame.LastModified.Should().Be(DateTimeOffset.Parse("2024-02-01T08:00:00Z"));
        wrappedGame.Tags.Should().BeEquivalentTo(new[] { "arcade", "retro" });

        var timeTravelers = entries.Should().ContainSingle(entry => entry.Id.StoreSpecificId == "wrapped:timetravelers").Subject;
        timeTravelers.SizeOnDisk.Should().Be(13579);
        timeTravelers.LastModified.Should().Be(DateTimeOffset.Parse("2024-03-15T11:20:00Z"));
        timeTravelers.Tags.Should().BeEquivalentTo(new[] { "rpg", "sci-fi", "time-travel" });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }

    private static string GetFixturePath(string relative)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private string PrepareCatalogDirectory(string fixtureRelativePath)
    {
        var catalogDirectory = Path.Combine(workingDirectory, Path.GetFileNameWithoutExtension(fixtureRelativePath) ?? "catalog");
        Directory.CreateDirectory(catalogDirectory);
        File.Copy(GetFixturePath(fixtureRelativePath), Path.Combine(catalogDirectory, Path.GetFileName(fixtureRelativePath)!));
        return catalogDirectory;
    }
}
