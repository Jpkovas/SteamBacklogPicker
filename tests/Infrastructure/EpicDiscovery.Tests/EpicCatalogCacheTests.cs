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
}
