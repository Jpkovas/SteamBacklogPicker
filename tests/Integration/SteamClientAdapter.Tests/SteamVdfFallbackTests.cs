using System;
using System.IO;
using SteamClientAdapter;
using ValveFormatParser;
using Xunit;

namespace SteamClientAdapter.Tests;

public sealed class SteamVdfFallbackTests
{
    private readonly string _steamRoot;

    public SteamVdfFallbackTests()
    {
        _steamRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "steam");
        EnsureAppInfoFixture();
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

    private SteamVdfFallback CreateFallback()
    {
        var accessor = new PhysicalFileAccessor();
        return new SteamVdfFallback(
            _steamRoot,
            accessor,
            new ValveTextVdfParser(),
            new ValveBinaryVdfParser());
    }

    private void EnsureAppInfoFixture()
    {
        var appCacheDirectory = Path.Combine(_steamRoot, "appcache");
        Directory.CreateDirectory(appCacheDirectory);

        var appInfoPath = Path.Combine(appCacheDirectory, "appinfo.vdf");
        if (File.Exists(appInfoPath))
        {
            var existingBytes = File.ReadAllBytes(appInfoPath);
            if (existingBytes.AsSpan().SequenceEqual(AppInfoFixtureBytes))
            {
                return;
            }
        }

        File.WriteAllBytes(appInfoPath, AppInfoFixtureBytes);
    }

    private static readonly byte[] AppInfoFixtureBytes = Convert.FromBase64String(
        "CgAAAC8AAAAAZXh0ZW5kZWQAAklzU3Vic2NyaWJlZEZyb21GYW1pbHlTaGFyaW5nAAAAAAAICBQAAAAvAAAAAGV4dGVuZGVkAAJJc1N1YnNjcmliZWRGcm9tRmFtaWx5U2hhcmluZwABAAAACAgAAAAAAAAAAA==");

    private sealed class PhysicalFileAccessor : IFileAccessor
    {
        public bool FileExists(string path) => File.Exists(path);

        public Stream OpenRead(string path) => File.OpenRead(path);

        public string ReadAllText(string path) => File.ReadAllText(path);
    }
}
