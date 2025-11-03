using System;
using System.IO;
using System.Linq;
using Domain;
using EpicDiscovery;
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
            entry.Tags.Contains("action"));
        entries.Should().Contain(entry =>
            entry.Id.StoreSpecificId == "rocket:rlgame" &&
            entry.Tags.Contains("sports") &&
            entry.Tags.Contains("soccer"));
    }

    [Fact]
    public void GetCatalogEntries_ShouldLoadFromSqliteCaches()
    {
        var catalogDirectory = Path.Combine(workingDirectory, "sqlite");
        Directory.CreateDirectory(catalogDirectory);
        var targetPath = Path.Combine(catalogDirectory, "catalog_cache.sqlite");
        WriteSqliteFixture(targetPath);

        using var cache = new EpicCatalogCache(
            new FakeEpicLauncherLocator(catalogDirectories: new[] { catalogDirectory }),
            new TestFileAccessor());

        var entries = cache.GetCatalogEntries();

        entries.Should().HaveCount(2);
        entries.Should().Contain(entry =>
            entry.Id.StoreSpecificId == "fn:fngame" &&
            entry.Title == "Fortnite Deluxe" &&
            entry.Tags.Contains("battle-royale"));
        entries.Should().Contain(entry =>
            entry.Id.StoreSpecificId == "rocket:rlgame" &&
            entry.Tags.Contains("sports"));
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

    private static void WriteSqliteFixture(string targetPath)
    {
        var base64 = File.ReadAllText(GetFixturePath("Catalog/catalog_cache.sqlite.b64"));
        var bytes = Convert.FromBase64String(base64);
        File.WriteAllBytes(targetPath, bytes);
    }
}
