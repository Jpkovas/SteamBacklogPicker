using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Library;
using SteamClientAdapter;
using SteamDiscovery;
using ValveFormatParser;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class SteamLibraryProviderTests
{
    [Fact]
    public async Task GetLibraryAsync_ShouldNotPromoteOwnershipOrInstallation_FromProfileHistory()
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

        var provider = new SteamLibraryProvider(cache, locator, fallback);

        var results = await provider.GetLibraryAsync();

        var entry = results.Should().ContainSingle(game => game.Id == GameIdentifier.ForSteam(appId)).Subject;
        entry.InstallState.Should().Be(InstallState.Unknown);
        entry.OwnershipType.Should().Be(OwnershipType.Unknown);
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
                [appId] = new SteamAppDefinition(appId, "Steam SDK", IsInstalled: false, Type: "application", Collections: Array.Empty<string>()) { InstallState = InstallState.Available }
            },
            sharedAppIds: Array.Empty<uint>());
        using var cache = new SteamAppManifestCache(locator, adapter, fallback, new ValveTextVdfParser());

        var provider = new SteamLibraryProvider(cache, locator, fallback);

        var results = await provider.GetLibraryAsync();

        var entry = results.Should().ContainSingle(game => game.Id == GameIdentifier.ForSteam(appId)).Subject;
        entry.ProductCategory.Should().Be(ProductCategory.Software);
        entry.InstallState.Should().Be(InstallState.Available);
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldAssignSupportedPlatforms_FromFallbackMetadata()
    {
        const uint appId = 6262;
        using var environment = new TestLibraryEnvironment();

        var locator = new FakeLibraryLocator(environment.LibraryRoot);
        var adapter = new FakeSteamClientAdapter(Array.Empty<uint>(), Array.Empty<uint>());
        var definition = new SteamAppDefinition(appId, "Mac Game", IsInstalled: false, Type: "game", Collections: Array.Empty<string>())
        {
            SupportedPlatforms = new[] { SteamPlatform.Windows, SteamPlatform.MacOS }
        };
        var fallback = new FakeSteamVdfFallback(
            new Dictionary<uint, SteamAppDefinition>
            {
                [appId] = definition
            },
            sharedAppIds: Array.Empty<uint>());
        using var cache = new SteamAppManifestCache(locator, adapter, fallback, new ValveTextVdfParser());

        var provider = new SteamLibraryProvider(cache, locator, fallback);

        var results = await provider.GetLibraryAsync();

        var entry = results.Should().ContainSingle(game => game.Id == GameIdentifier.ForSteam(appId)).Subject;
        entry.SupportedPlatforms.Should().BeEquivalentTo(new[] { SteamPlatform.Windows, SteamPlatform.MacOS });
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldKeepFamilyOwnershipSeparateFromInstallation()
    {
        using var environment = new TestLibraryEnvironment();
        environment.WriteManifest(10, "Installed family game");
        var locator = new FakeLibraryLocator(environment.LibraryRoot);
        var adapter = new FakeSteamClientAdapter(new[] { 10u }, Array.Empty<uint>());
        var fallback = new FakeSteamVdfFallback(new Dictionary<uint, SteamAppDefinition>
        {
            [10] = new(10, "Installed family game", false, "game", Array.Empty<string>())
                { OwnershipType = OwnershipType.FamilyShared, InstallState = InstallState.Available },
            [20] = new(20, "Available family game", true, "game", Array.Empty<string>())
                { OwnershipType = OwnershipType.FamilyShared, InstallState = InstallState.Available },
            [30] = new(30, "DLC", false, "dlc", Array.Empty<string>())
        }, Array.Empty<uint>(), collections: new[]
        {
            new SteamCollectionDefinition("installed", "Installed", Array.Empty<uint>(),
                new CollectionFilterSpec(new[] { new CollectionFilterGroup(new[] { 1 }, false) }))
        });
        using var cache = new SteamAppManifestCache(locator, adapter, fallback, new ValveTextVdfParser());
        var provider = new SteamLibraryProvider(cache, locator, fallback);
        var results = (await provider.GetLibraryAsync()).ToDictionary(game => game.SteamAppId!.Value);
        results[10].OwnershipType.Should().Be(OwnershipType.FamilyShared);
        results[10].InstallState.Should().Be(InstallState.Installed);
        results[10].Tags.Should().Contain("Installed");
        results[20].OwnershipType.Should().Be(OwnershipType.FamilyShared);
        results[20].InstallState.Should().Be(InstallState.Available);
        results[20].Tags.Should().NotContain("Installed");
        results[30].ProductCategory.Should().Be(ProductCategory.DLC);
    }

    [Theory]
    [InlineData(31, true)]
    [InlineData(52, true)]
    [InlineData(35, false)]
    [InlineData(38, false)]
    [InlineData(39, false)]
    [InlineData(53, true)]
    [InlineData(54, true)]
    public async Task GetLibraryAsync_ShouldNotConfuseUnrelatedStoreCategoriesWithVr(int category, bool expectedVr)
    {
        using var environment = new TestLibraryEnvironment();
        var locator = new FakeLibraryLocator(environment.LibraryRoot);
        var fallback = new FakeSteamVdfFallback(new Dictionary<uint, SteamAppDefinition>
        {
            [10] = new(10, "Game", false, "game", Array.Empty<string>()) { StoreCategoryIds = new[] { category } }
        }, Array.Empty<uint>(), collections: new[]
        {
            new SteamCollectionDefinition("vr", "VR", Array.Empty<uint>(),
                new CollectionFilterSpec(new[] { new CollectionFilterGroup(new[] { 3 }, false) }))
        });
        using var cache = new SteamAppManifestCache(locator, new FakeSteamClientAdapter(Array.Empty<uint>(), Array.Empty<uint>()), fallback, new ValveTextVdfParser());
        var result = (await new SteamLibraryProvider(cache, locator, fallback).GetLibraryAsync()).Single();
        result.Tags.Contains("VR").Should().Be(expectedVr);
    }

    private sealed class TestLibraryEnvironment : IDisposable
    {
        private readonly string root;

        public TestLibraryEnvironment()
        {
            root = Path.Combine(Path.GetTempPath(), "SteamLibraryProviderTests", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
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
        var provider = new SteamLibraryProvider(cache, locator, fallback);

        var results = await provider.GetLibraryAsync();

        var entry = results.Should().ContainSingle(game => game.Id == GameIdentifier.ForSteam(appId)).Subject;
        entry.Tags.Should().Contain("Jogáveis no Deck");
    }
}



