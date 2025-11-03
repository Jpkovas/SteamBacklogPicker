using System;
using System.IO;
using System.Linq;
using Domain;
using EpicDiscovery;
using FluentAssertions;
using Xunit;

namespace EpicDiscovery.Tests;

public sealed class EpicManifestCacheTests : IDisposable
{
    private readonly string workingDirectory;

    public EpicManifestCacheTests()
    {
        workingDirectory = Path.Combine(Path.GetTempPath(), "EpicManifestCacheTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
    }

    [Fact]
    public void GetInstalledGames_ShouldParseManifestEntries()
    {
        var manifestPath = Path.Combine(workingDirectory, "installed_game.item");
        File.Copy(GetFixturePath("Manifests/installed_game.item"), manifestPath);

        using var cache = new EpicManifestCache(
            new FakeEpicLauncherLocator(manifestDirectories: new[] { workingDirectory }),
            new TestFileAccessor());

        var games = cache.GetInstalledGames();

        games.Should().ContainSingle();
        var entry = games.Single();
        entry.Id.Storefront.Should().Be(Storefront.EpicGamesStore);
        entry.Id.StoreSpecificId.Should().Be("fn:fngame");
        entry.InstallState.Should().Be(InstallState.Installed);
        entry.SizeOnDisk.Should().Be(123456789);
        entry.LastPlayed.Should().Be(DateTimeOffset.Parse("2024-01-05T13:15:00Z"));
        entry.Tags.Should().Contain(new[] { "Shooter", "Battle Royale", "action", "multiplayer" });
    }

    [Fact]
    public void Refresh_ShouldDropEntriesForDeletedManifests()
    {
        var manifestPath = Path.Combine(workingDirectory, "installed_game.item");
        File.Copy(GetFixturePath("Manifests/installed_game.item"), manifestPath);

        using var cache = new EpicManifestCache(
            new FakeEpicLauncherLocator(manifestDirectories: new[] { workingDirectory }),
            new TestFileAccessor());

        cache.GetInstalledGames().Should().HaveCount(1);

        File.Delete(manifestPath);
        cache.Refresh();

        cache.GetInstalledGames().Should().BeEmpty();
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
