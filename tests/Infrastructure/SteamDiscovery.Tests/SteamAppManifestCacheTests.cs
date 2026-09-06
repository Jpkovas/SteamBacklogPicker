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
        Assert.Equal(GameIdentifier.ForSteam(42), game.Id);
        Assert.Equal(42u, game.SteamAppId);
        Assert.Equal("Test Game", game.Title);
        Assert.Equal(OwnershipType.Unknown, game.OwnershipType);
        Assert.Equal(InstallState.Installed, game.InstallState);
        Assert.Equal(1_500_000_000, game.SizeOnDisk);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), game.LastPlayed);
    }

    [Fact]
    public void GetInstalledGames_ShouldUseFullyInstalledFlag_WhenAdapterReturnsEmptySet()
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
        Assert.Equal(InstallState.Installed, game.InstallState);
    }

    [Fact]
    public void GetInstalledGames_ShouldNotInferFamilySharing_FromLastOwner()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(150, "Borrowed Game", 200, 0, lastOwner: "76561198000009999");

        using var cache = environment.CreateCache(new[] { 150u }, Array.Empty<uint>());
        var games = cache.GetInstalledGames();

        var game = Assert.Single(games);
        Assert.Equal(OwnershipType.Unknown, game.OwnershipType);
        Assert.Equal(InstallState.Installed, game.InstallState);
    }

    [Fact]
    public void GetInstalledGames_ShouldNotTreatSizeAsInstallationEvidence()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(123, "Sized Game", 1_000_000_000, 0, stateFlags: null);

        using var cache = environment.CreateCache(Array.Empty<uint>(), Array.Empty<uint>());
        var games = cache.GetInstalledGames();

        var game = Assert.Single(games);
        Assert.Equal(InstallState.Unknown, game.InstallState);
        Assert.Equal(1_000_000_000, game.SizeOnDisk);
    }

    [Fact]
    public void GetInstalledGames_ShouldPreferManifestEvidence_WhenAdapterOmitsAppId()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(303, "Unreported Game", 500_000_000, 0);

        using var cache = environment.CreateCache(new[] { 999u }, Array.Empty<uint>());
        var games = cache.GetInstalledGames();

        var game = Assert.Single(games);
        Assert.Equal(InstallState.Installed, game.InstallState);
        Assert.Equal(500_000_000, game.SizeOnDisk);
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


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Refresh_ShouldKeepEntry_WhenManifestIsRenamedWithCaseDifference(bool isWindows)
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(55, "Case Game", 321, 1700000300);

        var comparison = new PlatformPathComparisonStrategy(new FakePlatformProvider(isWindows, !isWindows));
        using var cache = environment.CreateCache(new[] { 55u }, Array.Empty<uint>(), comparison);
        Assert.Single(cache.GetInstalledGames());

        var oldPath = environment.GetManifestPath(55);
        var renamedPath = Path.Combine(environment.SteamAppsPath, "APPMANIFEST_55.ACF");
        File.Move(oldPath, renamedPath);

        cache.Refresh();
        var games = cache.GetInstalledGames();

        var game = Assert.Single(games);
        Assert.Equal("Case Game", game.Title);

        var pathIndexField = typeof(SteamAppManifestCache).GetField("_idByManifestPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(pathIndexField);
        var pathIndex = Assert.IsAssignableFrom<IReadOnlyDictionary<string, GameIdentifier>>(pathIndexField!.GetValue(cache));
        var indexedPaths = pathIndex.Keys.ToArray();
        Assert.Contains(renamedPath, indexedPaths);
        Assert.DoesNotContain(oldPath, indexedPaths);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    public void GetInstalledGames_ShouldExcludeIncompleteDownloads(int stateFlags)
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(33, "Partial download", 5_000_000, 0, stateFlags: stateFlags);
        using var cache = environment.CreateCache(Array.Empty<uint>(), Array.Empty<uint>());
        Assert.Equal(InstallState.Available, Assert.Single(cache.GetInstalledGames()).InstallState);
    }

    [Fact]
    public void Refresh_ShouldKeepMetadataButClearInstallation_WhenManifestBecomesTruncated()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(34, "Valid title", 5_000_000, 0);
        using var cache = environment.CreateCache(Array.Empty<uint>(), Array.Empty<uint>());
        Assert.Equal(InstallState.Installed, Assert.Single(cache.GetInstalledGames()).InstallState);
        File.WriteAllText(environment.GetManifestPath(34), "\"AppState\" {");
        cache.Refresh();
        var game = Assert.Single(cache.GetInstalledGames());
        Assert.Equal("Valid title", game.Title);
        Assert.Equal(InstallState.Unknown, game.InstallState);
    }

    [Fact]
    public void GetInstalledGames_ShouldRejectManifestWithMismatchedAppId()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(35, "Wrong identity", 10, 0);
        File.Move(environment.GetManifestPath(35), environment.GetManifestPath(36));
        using var cache = environment.CreateCache(Array.Empty<uint>(), Array.Empty<uint>());
        Assert.Empty(cache.GetInstalledGames());
    }

    [Fact]
    public void Refresh_ShouldInitializeCacheWithoutScanningAgainOnFirstRead()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(10, "Game", 10, 0);
        var adapter = new FakeSteamClientAdapter(Array.Empty<uint>(), Array.Empty<uint>());
        using var cache = new SteamAppManifestCache(
            new FakeLibraryLocator(Path.GetDirectoryName(environment.SteamAppsPath)!), adapter,
            new FakeSteamVdfFallback("76561198000000000", Array.Empty<uint>()), new ValveTextVdfParser());
        cache.Refresh();
        Assert.Single(cache.GetInstalledGames());
        Assert.Single(cache.GetInstalledGames());
        Assert.Equal(1, adapter.InstalledQueryCount);
    }

    [Fact]
    public void GetInstalledGames_ShouldRefreshAccountOwnershipOnUserSwitch()
    {
        using var environment = new ManifestTestEnvironment();
        environment.WriteManifest(10, "Game", 10, 0);
        var fallback = new FakeSteamVdfFallback("76561198000000000", new[] { 10u });
        using var cache = new SteamAppManifestCache(
            new FakeLibraryLocator(Path.GetDirectoryName(environment.SteamAppsPath)!),
            new FakeSteamClientAdapter(Array.Empty<uint>(), Array.Empty<uint>()), fallback, new ValveTextVdfParser());
        Assert.Equal(OwnershipType.FamilyShared, Assert.Single(cache.GetInstalledGames()).OwnershipType);
        fallback.SteamId = "76561198000000001";
        fallback.Shared = Array.Empty<uint>();
        var entry = Assert.Single(cache.GetInstalledGames());
        Assert.Equal(OwnershipType.Unknown, entry.OwnershipType);
        Assert.Equal(InstallState.Installed, entry.InstallState);
    }

    private sealed class ManifestTestEnvironment : IDisposable
    {
        private readonly string _root;
        private readonly FakeLibraryLocator _locator;
        private readonly ValveTextVdfParser _parser = new();
        private readonly string _steamId = "76561198000000000";

        public ManifestTestEnvironment()
        {
            _root = Path.Combine(Path.GetTempPath(), "SteamAppManifestCacheTests", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(SteamAppsPath);
            _locator = new FakeLibraryLocator(_root);
        }

        public string SteamAppsPath => Path.Combine(_root, "steamapps");

        public void WriteManifest(uint appId, string title, long sizeOnDisk, long lastPlayedSeconds, string? lastOwner = null, int? stateFlags = 4)
        {
            var manifestPath = GetManifestPath(appId);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

            lastOwner ??= _steamId;

            var content = "\"AppState\"\n{" +
                          $"\n    \"appid\" \"{appId}\"" +
                          $"\n    \"name\" \"{title}\"" +
                          $"\n    \"SizeOnDisk\" \"{sizeOnDisk}\"" +
                          $"\n    \"LastOwner\" \"{lastOwner}\"" +
                          "\n    \"UserConfig\"\n    {" +
                          $"\n        \"name\" \"{title}\"" +
                          $"\n        \"LastPlayed\" \"{lastPlayedSeconds}\"" +
                          "\n    }" +
                          "\n}";

            if (stateFlags.HasValue) content = content.Insert(content.LastIndexOf('}'), $"\n    \"StateFlags\" \"{stateFlags}\"\n");
            File.WriteAllText(manifestPath, content);
        }

        public string GetManifestPath(uint appId)
            => Path.Combine(SteamAppsPath, $"appmanifest_{appId}.acf");

        public SteamAppManifestCache CreateCache(IEnumerable<uint> installed, IEnumerable<uint> shared, IPathComparisonStrategy? pathComparison = null)
        {
            var adapter = new FakeSteamClientAdapter(installed, shared);
            var fallback = new FakeSteamVdfFallback(_steamId, shared);
            return pathComparison is null
                ? new SteamAppManifestCache(_locator, adapter, fallback, _parser)
                : new SteamAppManifestCache(_locator, adapter, fallback, _parser, pathComparison);
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

    private sealed class FakeSteamVdfFallback : ISteamVdfFallback
    {
        public string SteamId { get; set; }

        public uint[] Shared { get; set; }

        public FakeSteamVdfFallback(string steamId, IEnumerable<uint> shared)
        {
            SteamId = steamId;
            Shared = shared.ToArray();
        }

        public IReadOnlyCollection<uint> GetInstalledAppIds() => Array.Empty<uint>();

        public bool IsSubscribedFromFamilySharing(uint appId) => false;

        public IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps() => Shared.ToDictionary(id => id, id => new SteamAppDefinition(id, null, false, null, Array.Empty<string>()) { OwnershipType = OwnershipType.FamilyShared });

        public string? GetCurrentUserSteamId() => SteamId;

        public IReadOnlyList<SteamCollectionDefinition> GetCollections() => Array.Empty<SteamCollectionDefinition>();
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

    private sealed class FakePlatformProvider : IPlatformProvider
    {
        private readonly bool _isWindows;
        private readonly bool _isLinux;

        public FakePlatformProvider(bool isWindows, bool isLinux)
        {
            _isWindows = isWindows;
            _isLinux = isLinux;
        }

        public bool IsWindows() => _isWindows;

        public bool IsLinux() => _isLinux;
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

        public int InstalledQueryCount { get; private set; }

        public IReadOnlyCollection<uint> GetInstalledAppIds()
        {
            InstalledQueryCount++;
            return _installed;
        }

        public bool IsSubscribedFromFamilySharing(uint appId) => _shared.Contains(appId);

        public void Dispose()
        {
        }
    }
}

