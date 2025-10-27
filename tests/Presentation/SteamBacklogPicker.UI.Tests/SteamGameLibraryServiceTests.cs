using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services;
using SteamClientAdapter;
using SteamDiscovery;
using ValveFormatParser;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class SteamGameLibraryServiceTests
{
    [Fact]
    public async Task GetLibraryAsync_ShouldPromoteInstallState_WhenFallbackReportsInstallation()
    {
        const uint appId = 4242;
        using var environment = new TestLibraryEnvironment();
        environment.WriteManifest(appId, "Manifest Game");

        var locator = new FakeLibraryLocator(environment.LibraryRoot);
        var adapter = new FakeSteamClientAdapter(Array.Empty<uint>(), Array.Empty<uint>());
        var fallback = new FakeSteamVdfFallback(
            new Dictionary<uint, SteamAppDefinition>
            {
                [appId] = new SteamAppDefinition(appId, "Fallback Game", IsInstalled: true, Type: "game", Collections: Array.Empty<string>())
            },
            sharedAppIds: Array.Empty<uint>());
        using var cache = new SteamAppManifestCache(locator, adapter, fallback, new ValveTextVdfParser());

        var service = new SteamGameLibraryService(cache, locator, fallback);

        var results = await service.GetLibraryAsync();

        var entry = results.Should().ContainSingle(game => game.AppId == appId).Subject;
        entry.InstallState.Should().Be(InstallState.Installed);
        entry.OwnershipType.Should().Be(OwnershipType.Owned);
        entry.ProductCategory.Should().Be(ProductCategory.Game);
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldAssignProductCategory_FromFallbackType()
    {
        const uint appId = 5252;
        using var environment = new TestLibraryEnvironment();

        var locator = new FakeLibraryLocator(environment.LibraryRoot);
        var adapter = new FakeSteamClientAdapter(Array.Empty<uint>(), Array.Empty<uint>());
        var fallback = new FakeSteamVdfFallback(
            new Dictionary<uint, SteamAppDefinition>
            {
                [appId] = new SteamAppDefinition(appId, "Steam SDK", IsInstalled: false, Type: "application", Collections: Array.Empty<string>())
            },
            sharedAppIds: Array.Empty<uint>());
        using var cache = new SteamAppManifestCache(locator, adapter, fallback, new ValveTextVdfParser());

        var service = new SteamGameLibraryService(cache, locator, fallback);

        var results = await service.GetLibraryAsync();

        var entry = results.Should().ContainSingle(game => game.AppId == appId).Subject;
        entry.ProductCategory.Should().Be(ProductCategory.Software);
        entry.InstallState.Should().Be(InstallState.Available);
    }

    private sealed class TestLibraryEnvironment : IDisposable
    {
        private readonly string root;

        public TestLibraryEnvironment()
        {
            root = Path.Combine(Path.GetTempPath(), "SteamGameLibraryServiceTests", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(SteamAppsPath);
        }

        public string LibraryRoot => root;

        private string SteamAppsPath => Path.Combine(root, "steamapps");

        public void WriteManifest(uint appId, string title)
        {
            Directory.CreateDirectory(SteamAppsPath);

            var manifestPath = Path.Combine(SteamAppsPath, $"appmanifest_{appId}.acf");
            var content = $"\"AppState\"\n{{\n    \"appid\" \"{appId}\"\n    \"name\" \"{title}\"\n    \"UserConfig\"\n    {{\n        \"name\" \"{title}\"\n    }}\n}}";
            File.WriteAllText(manifestPath, content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors in test teardown.
            }
        }
    }

    private sealed class FakeLibraryLocator : ISteamLibraryLocator
    {
        private readonly IReadOnlyList<string> libraries;

        public FakeLibraryLocator(params string[] libraries)
        {
            this.libraries = libraries;
        }

        public IReadOnlyList<string> GetLibraryFolders() => libraries;

        public void Refresh()
        {
        }
    }

    private sealed class FakeSteamClientAdapter : ISteamClientAdapter
    {
        private readonly uint[] installed;
        private readonly HashSet<uint> shared;

        public FakeSteamClientAdapter(IEnumerable<uint> installedAppIds, IEnumerable<uint> sharedAppIds)
        {
            installed = installedAppIds?.ToArray() ?? Array.Empty<uint>();
            shared = sharedAppIds?.ToHashSet() ?? new HashSet<uint>();
        }

        public bool Initialize(string libraryPath) => true;

        public IReadOnlyCollection<uint> GetInstalledAppIds() => installed;

        public bool IsSubscribedFromFamilySharing(uint appId) => shared.Contains(appId);
    }

    private sealed class FakeSteamVdfFallback : ISteamVdfFallback
    {
        private readonly IReadOnlyDictionary<uint, SteamAppDefinition> apps;
        private readonly HashSet<uint> shared;
        private readonly string currentSteamId;
        private readonly IReadOnlyList<SteamCollectionDefinition> collections;

        public FakeSteamVdfFallback(
            IReadOnlyDictionary<uint, SteamAppDefinition> apps,
            IEnumerable<uint> sharedAppIds,
            string? currentSteamId = "76561198000000000",
            IReadOnlyList<SteamCollectionDefinition>? collections = null)
        {
            this.apps = apps ?? throw new ArgumentNullException(nameof(apps));
            shared = sharedAppIds?.ToHashSet() ?? new HashSet<uint>();
            this.currentSteamId = string.IsNullOrWhiteSpace(currentSteamId) ? "76561198000000000" : currentSteamId;
            this.collections = collections ?? Array.Empty<SteamCollectionDefinition>();
        }

        public IReadOnlyCollection<uint> GetInstalledAppIds() => apps.Keys.ToArray();

        public bool IsSubscribedFromFamilySharing(uint appId) => shared.Contains(appId);

        public IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps() => apps;

        public string? GetCurrentUserSteamId() => currentSteamId;

        public IReadOnlyList<SteamCollectionDefinition> GetCollections() => collections;
    }
    [Fact]
    public async Task GetLibraryAsync_ShouldTagGamesWithDynamicCollections()
    {
        const uint appId = 1313;
        using var environment = new TestLibraryEnvironment();
        environment.WriteManifest(appId, "Dynamic Game");

        var locator = new FakeLibraryLocator(environment.LibraryRoot);
        var adapter = new FakeSteamClientAdapter(Array.Empty<uint>(), Array.Empty<uint>());

        var definition = new SteamAppDefinition(appId, "Dynamic Game", IsInstalled: true, Type: "game", Collections: Array.Empty<string>())
        {
            DeckCompatibility = SteamDeckCompatibility.Verified
        };

        var filterSpec = new CollectionFilterSpec(new[]
        {
            new CollectionFilterGroup(new[] { 13 }, acceptUnion: false)
        });

        var fallback = new FakeSteamVdfFallback(
            new Dictionary<uint, SteamAppDefinition>
            {
                [appId] = definition
            },
            sharedAppIds: Array.Empty<uint>(),
            collections: new[]
            {
                new SteamCollectionDefinition("deck", "Jogáveis no Deck", Array.Empty<uint>(), filterSpec)
            });

        using var cache = new SteamAppManifestCache(locator, adapter, fallback, new ValveTextVdfParser());
        var service = new SteamGameLibraryService(cache, locator, fallback);

        var results = await service.GetLibraryAsync();

        var entry = results.Should().ContainSingle(game => game.AppId == appId).Subject;
        entry.Tags.Should().Contain("Jogáveis no Deck");
    }
}




