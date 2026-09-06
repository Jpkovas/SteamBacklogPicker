using System;
using System.IO;
using Domain;
using SteamClientAdapter;
using System.Linq;
using SteamTestUtilities.ValveFormat;
using ValveFormatParser;
using Xunit;

namespace SteamClientAdapter.Tests;

public sealed class SteamVdfFallbackTests : IDisposable
{
    private readonly string _steamRoot;

    public SteamVdfFallbackTests()
    {
        _steamRoot = Path.Combine(Path.GetTempPath(), "SteamVdfFallbackTests", Guid.NewGuid().ToString("N"));
        if (Directory.Exists(_steamRoot))
        {
            Directory.Delete(_steamRoot, recursive: true);
        }

        CopyDirectory(Path.Combine(VdfFixtureLoader.RootDirectory, "steam"), _steamRoot);
    }

    [Fact]
    public void GetKnownApps_FallsBackToAccountIdDirectories()
    {
        var userdata = Path.Combine(_steamRoot, "userdata");
        const string steamId = "76561198000000000";
        var original = Path.Combine(userdata, steamId);
        var accountIdValue = ulong.Parse(steamId) - 76561197960265728UL;
        var accountId = Path.Combine(userdata, accountIdValue.ToString());
        if (Directory.Exists(accountId))
        {
            Directory.Delete(accountId, recursive: true);
        }

        Directory.Move(original, accountId);

        var fallback = CreateFallback();

        var apps = fallback.GetKnownApps();

        Assert.Contains(10u, apps.Keys);
        Assert.Contains(20u, apps.Keys);
        Assert.Contains(30u, apps.Keys);
    }

    [Fact]
    public void GetInstalledAppIds_ShouldNotTreatProfileFlagsAsInstallationEvidence()
    {
        var fallback = CreateFallback();

        var appIds = fallback.GetInstalledAppIds();

        Assert.Empty(appIds);
    }

    [Fact]
    public void GetKnownApps_ReturnsEmpty_WhenLoginUsersVdfIsMalformed()
    {
        var loginUsersPath = Path.Combine(_steamRoot, "config", "loginusers.vdf");
        File.WriteAllText(loginUsersPath, "\"users\" { \"76561198000000000\" { ");

        var fallback = CreateFallback();

        var apps = fallback.GetKnownApps();

        Assert.Empty(apps);
    }

    [Theory]
    [InlineData(10u, false)]
    [InlineData(20u, true)]
    [InlineData(999u, false)]
    public void IsSubscribedFromFamilySharing_RespectsAppInfo(uint appId, bool expected)
    {
        var fallback = CreateFallback();

        var isShared = fallback.IsSubscribedFromFamilySharing(appId);

        Assert.Equal(expected, isShared);
    }

    [Fact]
    public void IsSubscribedFromFamilySharing_FallsBackToLocalConfig()
    {
        var fallbackRoot = Path.Combine(Path.GetTempPath(), "SteamVdfFallbackTests", Guid.NewGuid().ToString("N"));
        if (Directory.Exists(fallbackRoot))
        {
            Directory.Delete(fallbackRoot, recursive: true);
        }

        CopyDirectory(Path.Combine(VdfFixtureLoader.RootDirectory, "steam"), fallbackRoot);
        var appInfoPath = Path.Combine(fallbackRoot, "appcache", "appinfo.vdf");
        if (File.Exists(appInfoPath))
        {
            File.Delete(appInfoPath);
        }

        try
        {
            var accessor = new PhysicalFileAccessor();
            var fallback = new SteamVdfFallback(
                fallbackRoot,
                accessor,
                new ValveTextVdfParser(),
                new ValveBinaryVdfParser());

            _ = fallback.GetKnownApps();

            Assert.True(fallback.IsSubscribedFromFamilySharing(20u));
            Assert.False(fallback.IsSubscribedFromFamilySharing(10u));
        }
        finally
        {
            if (Directory.Exists(fallbackRoot))
            {
                Directory.Delete(fallbackRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void GetKnownApps_ReadsNestedLocalConfigAppsWithoutLibraryCacheOrAppInfo()
    {
        var appInfoPath = Path.Combine(_steamRoot, "appcache", "appinfo.vdf");
        if (File.Exists(appInfoPath))
        {
            File.Delete(appInfoPath);
        }

        var sharedConfigPath = Path.Combine(_steamRoot, "userdata", "76561198000000000", "7", "remote", "sharedconfig.vdf");
        if (File.Exists(sharedConfigPath))
        {
            File.Delete(sharedConfigPath);
        }

        var localConfigPath = Path.Combine(_steamRoot, "userdata", "76561198000000000", "config", "localconfig.vdf");
        File.WriteAllText(
            localConfigPath,
            """
            "UserLocalConfigStore"
            {
                "Software"
                {
                    "Valve"
                    {
                        "Steam"
                        {
                            "apps"
                            {
                                "40"
                                {
                                    "name"      "Nested Owned Installed"
                                    "Installed" "1"
                                }
                                "50"
                                {
                                    "name"      "Nested Owned Available"
                                    "Installed" "0"
                                }
                                "60"
                                {
                                    "name"      "Nested Family Available"
                                    "Installed" "0"
                                    "IsSubscribedFromFamilySharing" "1"
                                }
                            }
                        }
                    }
                }
            }
            """);

        var fallback = CreateFallback();

        var apps = fallback.GetKnownApps();

        Assert.True(apps.TryGetValue(40u, out var installed));
        Assert.False(installed.IsInstalled);
        Assert.Equal(InstallState.Available, installed.InstallState);
        Assert.Equal("Nested Owned Installed", installed.Name);

        Assert.True(apps.TryGetValue(50u, out var available));
        Assert.False(available.IsInstalled);
        Assert.Equal("Nested Owned Available", available.Name);

        Assert.True(apps.TryGetValue(60u, out var familyShared));
        Assert.False(familyShared.IsInstalled);
        Assert.Equal("Nested Family Available", familyShared.Name);
        Assert.True(fallback.IsSubscribedFromFamilySharing(60u));
    }

    [Fact]
    public void GetKnownApps_ShouldNotImportAppInfoOnlyFamilySharingFlags()
    {
        var localConfigPath = Path.Combine(_steamRoot, "userdata", "76561198000000000", "config", "localconfig.vdf");
        File.WriteAllText(localConfigPath, """
            "UserLocalConfigStore"
            {
                "apps"
                {
                    "10"
                    {
                        "name"      "Sample Game"
                        "Installed" "1"
                    }
                }
            }
            """);

        var sharedConfigPath = Path.Combine(_steamRoot, "userdata", "76561198000000000", "7", "remote", "sharedconfig.vdf");
        if (File.Exists(sharedConfigPath))
        {
            File.Delete(sharedConfigPath);
        }

        var fallback = CreateFallback();

        var apps = fallback.GetKnownApps();

        Assert.DoesNotContain(20u, apps.Keys);
        Assert.False(fallback.IsSubscribedFromFamilySharing(20u));
    }

    [Fact]
    public void GetKnownApps_ReturnsMetadataForAllDiscoveredTitles()
    {
        var fallback = CreateFallback();

        var apps = fallback.GetKnownApps();

        Assert.Equal(3, apps.Count);

        Assert.True(apps.TryGetValue(10u, out var ownedInstalled));
        Assert.False(ownedInstalled.IsInstalled);
        Assert.Equal(OwnershipType.Unknown, ownedInstalled.OwnershipType);
        Assert.Equal("Sample Game", ownedInstalled.Name);
        Assert.Contains("Favoritos", ownedInstalled.Collections);
        Assert.Contains("Jogáveis no Deck", ownedInstalled.Collections);

        Assert.True(apps.TryGetValue(20u, out var familyShared));
        Assert.False(familyShared.IsInstalled);
        Assert.Equal(OwnershipType.FamilyShared, familyShared.OwnershipType);
        Assert.Equal("Family Shared Game", familyShared.Name);
        Assert.Contains("Cooperativo", familyShared.Collections);
        Assert.Contains("VR", familyShared.Collections);

        Assert.True(apps.TryGetValue(30u, out var available));
        Assert.False(available.IsInstalled);
        Assert.Equal("Not Installed", available.Name);
        Assert.Contains("Backlog", available.Collections);
        Assert.Contains("Jogáveis no Deck", available.Collections);
    }

    [Fact]
    public void GetKnownApps_ReadsSupportedPlatformsFromAppInfoOsList()
    {
        var appInfoPath = Path.Combine(_steamRoot, "appcache", "appinfo.vdf");
        File.WriteAllBytes(
            appInfoPath,
            CreateLegacyAppInfoFixture(
                (10u, "Sample Game", "game", "windows,macos,linux"),
                (20u, "Family Shared Game", "game", "windows")));

        var fallback = CreateFallback();

        var apps = fallback.GetKnownApps();

        Assert.True(apps.TryGetValue(10u, out var macGame));
        Assert.Equal(new[] { SteamPlatform.Windows, SteamPlatform.MacOS, SteamPlatform.Linux }, macGame.SupportedPlatforms);

        Assert.True(apps.TryGetValue(20u, out var windowsGame));
        Assert.Equal(new[] { SteamPlatform.Windows }, windowsGame.SupportedPlatforms);
    }

    [Fact]
    public void GetKnownApps_ShouldKeepMissingInstalledAndOwnershipUnknown()
    {
        File.WriteAllText(Path.Combine(_steamRoot, "userdata", "76561198000000000", "config", "localconfig.vdf"),
            "\"UserLocalConfigStore\" { \"apps\" { \"90\" { \"name\" \"Played before\" } } }");
        var app = CreateFallback().GetKnownApps()[90];
        Assert.False(app.IsInstalled);
        Assert.Equal(InstallState.Available, app.InstallState);
        Assert.Equal(OwnershipType.Unknown, app.OwnershipType);
    }

    [Fact]
    public void GetKnownApps_ShouldInvalidateAccountAndCollectionsWhenActiveUserChanges()
    {
        var fallback = CreateFallback();
        Assert.True(fallback.IsSubscribedFromFamilySharing(20));
        var second = Path.Combine(_steamRoot, "userdata", "76561198000000001", "config");
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "localconfig.vdf"),
            "\"UserLocalConfigStore\" { \"apps\" { \"99\" { \"name\" \"Second account\" \"is_owned\" \"1\" } } }");
        File.WriteAllText(Path.Combine(_steamRoot, "config", "loginusers.vdf"),
            "\"users\" { \"76561198000000001\" { \"MostRecent\" \"1\" } }");
        var apps = fallback.GetKnownApps();
        Assert.Equal("76561198000000001", fallback.GetCurrentUserSteamId());
        Assert.Equal(new[] { 99u }, apps.Keys);
        Assert.Equal(OwnershipType.Owned, apps[99].OwnershipType);
        Assert.False(fallback.IsSubscribedFromFamilySharing(20));
        Assert.Empty(fallback.GetCollections());
    }

    [Fact]
    public void GetCurrentUserSteamId_ShouldRejectNonNumericAndNonIndividualIds()
    {
        File.WriteAllText(Path.Combine(_steamRoot, "config", "loginusers.vdf"),
            "\"users\" { \"../other\" { \"MostRecent\" \"1\" } \"123\" { \"Timestamp\" \"1700000000\" } }");
        var fallback = CreateFallback();
        Assert.Null(fallback.GetCurrentUserSteamId());
        Assert.Empty(fallback.GetKnownApps());
    }

    [Fact]
    public void GetKnownApps_ShouldReuseUnchangedSnapshotAndReloadReplacedAppInfo()
    {
        var path = Path.Combine(_steamRoot, "appcache", "appinfo.vdf");
        File.WriteAllBytes(path, CreateLegacyAppInfoFixture((10u, "Official name", "game", "windows")));
        var fallback = CreateFallback();
        var first = fallback.GetKnownApps();
        Assert.Equal("Official name", first[10].Name);
        Assert.Same(first, fallback.GetKnownApps());
        File.WriteAllBytes(path, CreateLegacyAppInfoFixture((10u, "Updated official title", "game", "linux")));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
        var updated = fallback.GetKnownApps();
        Assert.Equal("Updated official title", updated[10].Name);
        Assert.Equal(new[] { SteamPlatform.Linux }, updated[10].SupportedPlatforms);
    }

    [Fact]
    public void GetKnownApps_ShouldReadLibraryCacheJsonWithoutInferringInstallationOrOwnership()
    {
        var directory = Path.Combine(_steamRoot, "userdata", "76561198000000000", "config", "librarycache");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "90.json"),
            "{\"data\":{\"name\":\"Cached title\",\"app_type\":\"tool\",\"is_installed\":true}}");
        var fallback = CreateFallback();
        var app = fallback.GetKnownApps()[90];
        Assert.Equal("Cached title", app.Name);
        Assert.Equal("tool", app.Type);
        Assert.False(app.IsInstalled);
        Assert.Equal(OwnershipType.Unknown, app.OwnershipType);
        File.WriteAllText(Path.Combine(directory, "91.json"), "{ broken");
        Assert.Contains(90u, fallback.GetKnownApps().Keys);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[[5,{}]]")]
    [InlineData("[[\"user-collections.x\",{\"value\":\"{\\\"id\\\":5}\"}]]")]
    [InlineData("[[\"user-collections.x\",{\"value\":\"{\\\"id\\\":\\\"x\\\",\\\"filterSpec\\\":5}\"}]]")]
    public void GetCollections_ShouldIgnoreWrongJsonTypes(string json)
    {
        var directory = Path.Combine(_steamRoot, "userdata", "76561198000000000", "config", "cloudstorage");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "cloud-storage-namespace-1.json"), json);
        var fallback = CreateFallback();
        Assert.NotEmpty(fallback.GetKnownApps());
        _ = fallback.GetCollections();
    }

    [Theory]
    [InlineData("{\"related_apps\":[{\"name\":\"Other game\",\"is_owned\":true,\"is_family_shared\":true}]}", null)]
    [InlineData("[[\"descriptions\",{\"name\":\"Other name\"}],[\"appinfo\",{\"common\":{\"name\":\"Section name\"}}]]", "Section name")]
    public void GetKnownApps_ShouldOnlyReadMetadataFromRecognizedAppSections(string json, string? expectedName)
    {
        var directory = Path.Combine(_steamRoot, "userdata", "76561198000000000", "config", "librarycache");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "90.json"), json);
        var app = CreateFallback().GetKnownApps()[90];
        Assert.Equal(expectedName, app.Name);
        Assert.Equal(OwnershipType.Unknown, app.OwnershipType);
    }

    [Fact]
    public void GetKnownApps_ShouldIgnoreInvalidZeroAppIdWithOwnershipFlag()
    {
        File.WriteAllText(Path.Combine(_steamRoot, "userdata", "76561198000000000", "config", "localconfig.vdf"),
            "\"UserLocalConfigStore\" { \"apps\" { \"0\" { \"is_owned\" \"1\" } } }");
        Assert.DoesNotContain(0u, CreateFallback().GetKnownApps().Keys);
    }

    [Theory]
    [InlineData(0, SteamDeckCompatibility.Unknown)]
    [InlineData(1, SteamDeckCompatibility.Unsupported)]
    [InlineData(2, SteamDeckCompatibility.Playable)]
    [InlineData(3, SteamDeckCompatibility.Verified)]
    public void GetKnownApps_ShouldTranslateValveDeckCategoriesWithoutReorderingDomainValues(int rawCategory, SteamDeckCompatibility expected)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)0);
            WriteNullTerminatedString(writer, "common");
            writer.Write((byte)0);
            WriteNullTerminatedString(writer, "steam_deck_compatibility");
            writer.Write((byte)2);
            WriteNullTerminatedString(writer, "category");
            writer.Write(rawCategory);
            writer.Write(new byte[] { 8, 8, 8 });
        }
        using var appinfo = new MemoryStream();
        using (var writer = new BinaryWriter(appinfo, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(10u);
            writer.Write((uint)(40 + payload.Length));
            writer.Write(new byte[40]);
            writer.Write(payload.ToArray());
            writer.Write(0u);
            writer.Write(0u);
        }
        File.WriteAllBytes(Path.Combine(_steamRoot, "appcache", "appinfo.vdf"), appinfo.ToArray());
        Assert.Equal(expected, CreateFallback().GetKnownApps()[10].DeckCompatibility);
    }

    public void Dispose()
    {
        if (Directory.Exists(_steamRoot))
        {
            Directory.Delete(_steamRoot, recursive: true);
        }
    }

    private SteamVdfFallback CreateFallback()
    {
        var accessor = new PhysicalFileAccessor();
        return new SteamVdfFallback(
            _steamRoot,
            accessor,
            new ValveTextVdfParser(),
            new ValveBinaryVdfParser());
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var target = Path.Combine(destinationDirectory, Path.GetFileName(file)!);
            File.Copy(file, target, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            var name = Path.GetFileName(directory)!;
            CopyDirectory(directory, Path.Combine(destinationDirectory, name));
        }
    }

    private static byte[] CreateLegacyAppInfoFixture(params (uint AppId, string Name, string Type, string OsList)[] entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        foreach (var entry in entries)
        {
            var payload = CreateLegacyAppInfoPayload(entry.Name, entry.Type, entry.OsList);
            writer.Write(entry.AppId);
            writer.Write((uint)(40 + payload.Length));
            writer.Write(new byte[40]);
            writer.Write(payload);
        }

        writer.Write(0u);
        writer.Write(0u);
        return stream.ToArray();
    }

    private static byte[] CreateLegacyAppInfoPayload(string name, string type, string osList)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)0x00);
        WriteNullTerminatedString(writer, "common");

        writer.Write((byte)0x01);
        WriteNullTerminatedString(writer, "name");
        WriteNullTerminatedString(writer, name);

        writer.Write((byte)0x01);
        WriteNullTerminatedString(writer, "type");
        WriteNullTerminatedString(writer, type);

        writer.Write((byte)0x01);
        WriteNullTerminatedString(writer, "oslist");
        WriteNullTerminatedString(writer, osList);

        writer.Write((byte)0x08);
        writer.Write((byte)0x08);
        return stream.ToArray();
    }

    private static void WriteNullTerminatedString(BinaryWriter writer, string value)
    {
        writer.Write(System.Text.Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private sealed class PhysicalFileAccessor : IFileAccessor
    {
        public bool FileExists(string path) => File.Exists(path);

        public Stream OpenRead(string path) => File.OpenRead(path);

        public string ReadAllText(string path) => File.ReadAllText(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteAllBytes(string path, byte[] contents) => File.WriteAllBytes(path, contents);
    }
}
