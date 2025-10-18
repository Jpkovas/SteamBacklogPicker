using System;
using System.Collections.Generic;
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
    public void IsSubscribedFromFamilySharing_FallsBackToLocalConfig()
    {
        var fallbackRoot = Path.Combine(AppContext.BaseDirectory, "steam-fixture-local");
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
    public void Invalidate_ReloadsAppInfoMetadata()
    {
        var fallback = CreateFallback();

        _ = fallback.GetKnownApps();
        Assert.True(fallback.IsSubscribedFromFamilySharing(20u));

        fallback.Invalidate();
        _ = fallback.GetKnownApps();

        Assert.True(fallback.IsSubscribedFromFamilySharing(20u));
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
        Assert.Contains("Jogáveis no Deck", ownedInstalled.Collections);

        Assert.True(apps.TryGetValue(20u, out var familyShared));
        Assert.True(familyShared.IsInstalled);
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
    public void GetKnownApps_ReusesCacheUntilDependenciesChange()
    {
        var accessor = new CountingFileAccessor();
        var fallback = new SteamVdfFallback(
            _steamRoot,
            accessor,
            new ValveTextVdfParser(),
            new ValveBinaryVdfParser());

        var loginUsersPath = Path.Combine(_steamRoot, "config", "loginusers.vdf");
        var localConfigPath = Path.Combine(
            _steamRoot,
            "userdata",
            "76561198000000000",
            "config",
            "localconfig.vdf");

        _ = fallback.GetKnownApps();

        var initialLoginReads = accessor.GetReadAllTextCount(loginUsersPath);
        var initialLocalReads = accessor.GetReadAllTextCount(localConfigPath);

        _ = fallback.GetKnownApps();

        Assert.Equal(initialLoginReads, accessor.GetReadAllTextCount(loginUsersPath));
        Assert.Equal(initialLocalReads, accessor.GetReadAllTextCount(localConfigPath));

        var updatedTimestamp = File.GetLastWriteTimeUtc(localConfigPath).AddMinutes(1);
        File.SetLastWriteTimeUtc(localConfigPath, updatedTimestamp);

        _ = fallback.GetKnownApps();

        Assert.True(accessor.GetReadAllTextCount(localConfigPath) > initialLocalReads);
        Assert.True(accessor.GetReadAllTextCount(loginUsersPath) > initialLoginReads);
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

    private sealed class CountingFileAccessor : IFileAccessor
    {
        private readonly object sync = new();
        private readonly Dictionary<string, int> readCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> openCounts = new(StringComparer.OrdinalIgnoreCase);

        public int GetReadAllTextCount(string path)
        {
            lock (sync)
            {
                return readCounts.TryGetValue(path, out var count) ? count : 0;
            }
        }

        public bool FileExists(string path) => File.Exists(path);

        public Stream OpenRead(string path)
        {
            Increment(openCounts, path);
            return File.OpenRead(path);
        }

        public string ReadAllText(string path)
        {
            Increment(readCounts, path);
            return File.ReadAllText(path);
        }

        private void Increment(Dictionary<string, int> counts, string path)
        {
            lock (sync)
            {
                counts[path] = counts.TryGetValue(path, out var existing) ? existing + 1 : 1;
            }
        }
    }
}
