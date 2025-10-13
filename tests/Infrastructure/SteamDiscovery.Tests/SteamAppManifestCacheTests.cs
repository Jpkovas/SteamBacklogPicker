using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Domain;
using SteamClientAdapter;
using SteamDiscovery;
using ValveFormatParser;
using Xunit;

namespace SteamDiscovery.Tests;

public sealed class SteamAppManifestCacheTests
{
    [Fact]
    public void GetInstalledGames_LoadsManifestData()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(42, "Test Game", 1_500_000_000, 1700000000);

        using var cache = environment.CreateCache(new[] { 42u }, Array.Empty<uint>());
        var games = cache.GetInstalledGames();

        var game = Assert.Single(games);
        Assert.Equal(42u, game.AppId);
        Assert.Equal("Test Game", game.Title);
        Assert.Equal(OwnershipType.Owned, game.OwnershipType);
        Assert.Equal(InstallState.Installed, game.InstallState);
        Assert.Equal(1_500_000_000, game.SizeOnDisk);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), game.LastPlayed);
    }

    [Fact]
    public void GetInstalledGames_TreatsManifestAsInstalled_WhenAdapterReturnsEmptySet()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(101, "Offline Game", 2_000_000_000, 0);

        using var cache = environment.CreateCache(Array.Empty<uint>(), Array.Empty<uint>());
        var games = cache.GetInstalledGames();

        var game = Assert.Single(games);
        Assert.Equal(InstallState.Installed, game.InstallState);
    }

    [Fact]
    public void GetInstalledGames_ClassifiesFamilySharing()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(99, "Shared Game", 100, 0);

        using var cache = environment.CreateCache(new[] { 99u }, new[] { 99u });
        var games = cache.GetInstalledGames();

        var game = Assert.Single(games);
        Assert.Equal(OwnershipType.FamilyShared, game.OwnershipType);
        Assert.Equal(InstallState.Shared, game.InstallState);
    }

    [Fact]
    public void Cache_UpdatesIncrementally_OnManifestChange()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(7, "Old Name", 50, 1700000100);

        using var cache = environment.CreateCache(new[] { 7u }, Array.Empty<uint>());
        _ = cache.GetInstalledGames();

        environment.WriteManifest(7, "New Name", 50, 1700000100);

        var updated = SpinWait.SpinUntil(() =>
        {
            Thread.Sleep(50);
            var entry = cache.GetInstalledGames().Single();
            return entry.Title == "New Name";
        }, TimeSpan.FromSeconds(3));

        Assert.True(updated, "Cache did not refresh after manifest change.");
    }

    [Fact]
    public void Cache_RemovesEntry_OnManifestDeletion()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(11, "Temp Game", 123, 1700000200);

        using var cache = environment.CreateCache(new[] { 11u }, Array.Empty<uint>());
        Assert.Single(cache.GetInstalledGames());

        File.Delete(environment.GetManifestPath(11));

        var removed = SpinWait.SpinUntil(() =>
        {
            Thread.Sleep(50);
            return cache.GetInstalledGames().Count == 0;
        }, TimeSpan.FromSeconds(3));

        Assert.True(removed, "Cache did not remove entry after manifest deletion.");
    }

    private sealed class ManifestTestEnvironment : IDisposable
    {
        private readonly string _root;
        private readonly FakeLibraryLocator _locator;
        private readonly ValveTextVdfParser _parser = new();

        public ManifestTestEnvironment()
        {
            _root = Path.Combine(Path.GetTempPath(), "SteamAppManifestCacheTests", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(SteamAppsPath);
            _locator = new FakeLibraryLocator(_root);
        }

        public string SteamAppsPath => Path.Combine(_root, "steamapps");

        public void WriteManifest(uint appId, string title, long sizeOnDisk, long lastPlayedSeconds)
        {
            var manifestPath = GetManifestPath(appId);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

            var content = "\"AppState\"\n{" +
                          $"\n    \"appid\" \"{appId}\"" +
                          $"\n    \"name\" \"{title}\"" +
                          $"\n    \"SizeOnDisk\" \"{sizeOnDisk}\"" +
                          "\n    \"UserConfig\"\n    {" +
                          $"\n        \"name\" \"{title}\"" +
                          $"\n        \"LastPlayed\" \"{lastPlayedSeconds}\"" +
                          "\n    }" +
                          "\n}";

            File.WriteAllText(manifestPath, content);
        }

        public string GetManifestPath(uint appId)
            => Path.Combine(SteamAppsPath, $"appmanifest_{appId}.acf");

        public SteamAppManifestCache CreateCache(IEnumerable<uint> installed, IEnumerable<uint> shared)
        {
            var adapter = new FakeSteamClientAdapter(installed, shared);
            return new SteamAppManifestCache(_locator, adapter, _parser);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }

    private sealed class FakeLibraryLocator : ISteamLibraryLocator
    {
        private readonly IReadOnlyList<string> _libraries;

        public FakeLibraryLocator(params string[] libraries)
        {
            _libraries = libraries;
        }

        public IReadOnlyList<string> GetLibraryFolders() => _libraries;

        public void Refresh()
        {
        }
    }

    private sealed class FakeSteamClientAdapter : ISteamClientAdapter
    {
        private readonly HashSet<uint> _installed;
        private readonly HashSet<uint> _shared;

        public FakeSteamClientAdapter(IEnumerable<uint> installed, IEnumerable<uint> shared)
        {
            _installed = installed?.ToHashSet() ?? new HashSet<uint>();
            _shared = shared?.ToHashSet() ?? new HashSet<uint>();
        }

        public bool Initialize(string libraryPath) => true;

        public IReadOnlyCollection<uint> GetInstalledAppIds() => _installed;

        public bool IsSubscribedFromFamilySharing(uint appId) => _shared.Contains(appId);

        public void Dispose()
        {
        }
    }
}
