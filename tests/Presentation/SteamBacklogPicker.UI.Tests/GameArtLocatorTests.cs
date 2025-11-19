using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Domain;
using EpicDiscovery;
using FluentAssertions;
using SteamBacklogPicker.UI.Services;
using SteamClientAdapter;
using SteamDiscovery;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class GameArtLocatorTests
{
    [Fact]
    public void EpicGameArtLocator_ShouldPreferLocalPath_WhenAvailable()
    {
        using var context = EpicCatalogContext.Create(includeLocalImage: true, includeRemoteImage: true);
        var locator = context.CreateEpicLocator();
        var game = context.CreateEpicGame();

        var result = locator.FindHeroImage(game);

        result.Should().Be(context.LocalImagePath);
    }

    [Fact]
    public void EpicGameArtLocator_ShouldFallbackToRemoteUri_WhenLocalFileMissing()
    {
        using var context = EpicCatalogContext.Create(includeLocalImage: false, includeRemoteImage: true);
        var locator = context.CreateEpicLocator();
        var game = context.CreateEpicGame();

        var result = locator.FindHeroImage(game);

        result.Should().Be(context.RemoteImageUri);
    }

    [Fact]
    public void EpicGameArtLocator_ShouldReturnNull_WhenNoKeyImages()
    {
        using var context = EpicCatalogContext.Create(includeLocalImage: false, includeRemoteImage: false, includeImages: false);
        var locator = context.CreateEpicLocator();
        var game = context.CreateEpicGame();

        var result = locator.FindHeroImage(game);

        result.Should().BeNull();
    }

    [Fact]
    public void CompositeGameArtLocator_ShouldReturnSteamArt_ForSteamGames()
    {
        using var context = EpicCatalogContext.Create(includeLocalImage: true, includeRemoteImage: true);
        var steamLocator = new SteamGameArtLocator(new FakeSteamLibraryLocator());
        var epicLocator = context.CreateEpicLocator();
        var composite = new CompositeGameArtLocator(steamLocator, epicLocator);
        var steamGame = new GameEntry
        {
            Id = GameIdentifier.ForSteam(123),
            Title = "Steam Game"
        };

        var result = composite.FindHeroImage(steamGame);

        result.Should().Be("https://cdn.cloudflare.steamstatic.com/steam/apps/123/header.jpg");
    }

    [Fact]
    public void CompositeGameArtLocator_ShouldReturnEpicArt_ForEpicGames()
    {
        using var context = EpicCatalogContext.Create(includeLocalImage: false, includeRemoteImage: true);
        var steamLocator = new SteamGameArtLocator(new FakeSteamLibraryLocator());
        var epicLocator = context.CreateEpicLocator();
        var composite = new CompositeGameArtLocator(steamLocator, epicLocator);
        var epicGame = context.CreateEpicGame();

        var result = composite.FindHeroImage(epicGame);

        result.Should().Be(context.RemoteImageUri);
    }

    private sealed class EpicCatalogContext : IDisposable
    {
        private readonly string workingDirectory;
        private readonly IFileAccessor fileAccessor;
        private readonly EpicCatalogCache catalogCache;
        private readonly string catalogItemId;
        private readonly string catalogNamespace;

        private EpicCatalogContext(bool includeImages, bool includeLocalImage, bool includeRemoteImage)
        {
            workingDirectory = Path.Combine(Path.GetTempPath(), "EpicGameArtLocatorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            catalogNamespace = "testns";
            catalogItemId = "testapp";
            LocalImagePath = includeLocalImage
                ? Path.Combine(workingDirectory, "images", "cover.jpg")
                : null;
            RemoteImageUri = includeRemoteImage ? "https://example.com/hero.jpg" : null;

            if (LocalImagePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LocalImagePath)!);
                File.WriteAllText(LocalImagePath, "local-cover");
            }

            var element = new Dictionary<string, object?>
            {
                ["catalogNamespace"] = catalogNamespace,
                ["catalogItemId"] = catalogItemId,
                ["appName"] = "TestApp",
                ["displayName"] = "Test App"
            };

            if (includeImages)
            {
                var image = new Dictionary<string, object?>
                {
                    ["type"] = "DieselGameBox"
                };

                if (includeRemoteImage)
                {
                    image["url"] = RemoteImageUri;
                }

                if (includeLocalImage)
                {
                    image["path"] = LocalImagePath;
                }

                element["keyImages"] = new object[] { image };
            }

            var root = new Dictionary<string, object?>
            {
                ["elements"] = new[] { element }
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(root, options);
            File.WriteAllText(Path.Combine(workingDirectory, "catalog.json"), json);

            fileAccessor = new DefaultFileAccessor();
            var launcherLocator = new StaticEpicLauncherLocator(workingDirectory);
            catalogCache = new EpicCatalogCache(launcherLocator, fileAccessor);
            GameIdentifier = new GameIdentifier
            {
                Storefront = Storefront.EpicGamesStore,
                StoreSpecificId = $"{catalogNamespace}:{catalogItemId}"
            };
        }

        public string? LocalImagePath { get; }

        public string? RemoteImageUri { get; }

        public GameIdentifier GameIdentifier { get; }

        public static EpicCatalogContext Create(bool includeLocalImage, bool includeRemoteImage, bool includeImages = true)
        {
            return new EpicCatalogContext(includeImages, includeLocalImage, includeRemoteImage);
        }

        public EpicGameArtLocator CreateEpicLocator()
        {
            return new EpicGameArtLocator(catalogCache, fileAccessor);
        }

        public GameEntry CreateEpicGame()
        {
            return new GameEntry
            {
                Id = GameIdentifier,
                Title = "Test App"
            };
        }

        public void Dispose()
        {
            catalogCache.Dispose();

            try
            {
                if (Directory.Exists(workingDirectory))
                {
                    Directory.Delete(workingDirectory, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup errors in tests
            }
        }
    }

    private sealed class StaticEpicLauncherLocator : IEpicLauncherLocator
    {
        private readonly IReadOnlyCollection<string> catalogDirectories;

        public StaticEpicLauncherLocator(params string[] catalogDirectories)
        {
            this.catalogDirectories = catalogDirectories ?? Array.Empty<string>();
        }

        public IReadOnlyCollection<string> GetManifestDirectories() => Array.Empty<string>();

        public IReadOnlyCollection<string> GetCatalogDirectories() => catalogDirectories;

        public string? GetLauncherInstalledDatPath() => null;
    }

    private sealed class FakeSteamLibraryLocator : ISteamLibraryLocator
    {
        public IReadOnlyList<string> GetLibraryFolders() => Array.Empty<string>();

        public void Refresh()
        {
        }
    }
}
