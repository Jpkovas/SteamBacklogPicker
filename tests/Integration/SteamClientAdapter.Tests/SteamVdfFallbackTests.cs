using System;
using System.IO;
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
        _steamRoot = Path.Combine(AppContext.BaseDirectory, "steam-fixture");
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
    public void GetInstalledAppIds_ReadsInstalledFlags()
    {
        var fallback = CreateFallback();

        var appIds = fallback.GetInstalledAppIds();

        Assert.Equal(new uint[] { 10, 20 }, appIds);
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
    public void GetKnownApps_ReturnsMetadataForAllDiscoveredTitles()
    {
        var fallback = CreateFallback();

        var apps = fallback.GetKnownApps();

        Assert.Equal(3, apps.Count);

        Assert.True(apps.TryGetValue(10u, out var ownedInstalled));
        Assert.True(ownedInstalled.IsInstalled);
        Assert.Equal("Sample Game", ownedInstalled.Name);
        Assert.Contains("Favoritos", ownedInstalled.Collections);

        Assert.True(apps.TryGetValue(20u, out var familyShared));
        Assert.True(familyShared.IsInstalled);
        Assert.Equal("Family Shared Game", familyShared.Name);
        Assert.Contains("Cooperativo", familyShared.Collections);

        Assert.True(apps.TryGetValue(30u, out var available));
        Assert.False(available.IsInstalled);
        Assert.Equal("Not Installed", available.Name);
        Assert.Contains("Backlog", available.Collections);
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

    private sealed class PhysicalFileAccessor : IFileAccessor
    {
        public bool FileExists(string path) => File.Exists(path);

        public Stream OpenRead(string path) => File.OpenRead(path);

        public string ReadAllText(string path) => File.ReadAllText(path);
    }
}
