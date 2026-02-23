using System.Collections.Generic;
using SteamDiscovery;
using Xunit;

namespace SteamDiscovery.Tests;

public sealed class PlatformPathComparisonStrategyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Equals_ShouldRespectPlatformPathCasing(bool isWindows)
    {
        var strategy = new PlatformPathComparisonStrategy(new FakePlatformProvider(isWindows, !isWindows));

        var result = strategy.Equals("/tmp/SteamApps/AppManifest_10.acf", "/tmp/steamapps/appmanifest_10.acf");

        Assert.Equal(isWindows, result);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void Comparer_ShouldControlPathLookupCollisions(bool isWindows, int expectedCount)
    {
        var strategy = new PlatformPathComparisonStrategy(new FakePlatformProvider(isWindows, !isWindows));
        var lookup = new Dictionary<string, int>(strategy.Comparer)
        {
            ["/tmp/SteamApps/appmanifest_10.acf"] = 10,
            ["/tmp/steamapps/APPMANIFEST_10.ACF"] = 11
        };

        Assert.Equal(expectedCount, lookup.Count);
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
}
