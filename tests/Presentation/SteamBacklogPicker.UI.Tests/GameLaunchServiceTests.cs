using Domain;
using EpicDiscovery;
using FluentAssertions;
using SteamBacklogPicker.UI.Services;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class GameLaunchServiceTests
{
    [Fact]
    public void GetLaunchOptions_ForEpicInstalledGame_ProducesLaunchUri()
    {
        var identifier = new GameIdentifier
        {
            Storefront = Storefront.EpicGamesStore,
            StoreSpecificId = "epicnamespace:rocketleague",
        };

        var entry = new GameEntry
        {
            Id = identifier,
            Title = "Rocket League",
            InstallState = InstallState.Installed,
        };

        var catalogItem = new EpicCatalogItem
        {
            Id = identifier,
            AppName = "RocketLeague",
            CatalogItemId = "rocketleague",
            CatalogNamespace = "epicnamespace",
        };

        var service = new GameLaunchService(id => id.Equals(identifier) ? catalogItem : null);

        var options = service.GetLaunchOptions(entry);

        options.Launch.IsSupported.Should().BeTrue();
        options.Launch.ProtocolUri.Should().Be("com.epicgames.launcher://apps/RocketLeague?action=launch&silent=true");
        options.Install.IsSupported.Should().BeFalse();
        options.EpicAppName.Should().Be("RocketLeague");
        options.EpicCatalogItemId.Should().Be("rocketleague");
    }

    [Fact]
    public void GetLaunchOptions_ForEpicAvailableGame_ProducesInstallUri()
    {
        var identifier = new GameIdentifier
        {
            Storefront = Storefront.EpicGamesStore,
            StoreSpecificId = "epicnamespace:fortnite",
        };

        var entry = new GameEntry
        {
            Id = identifier,
            Title = "Fortnite",
            InstallState = InstallState.Available,
        };

        var catalogItem = new EpicCatalogItem
        {
            Id = identifier,
            AppName = "Fortnite",
            CatalogItemId = "fortnite",
            CatalogNamespace = "epicnamespace",
        };

        var service = new GameLaunchService(_ => catalogItem);

        var options = service.GetLaunchOptions(entry);

        options.Install.IsSupported.Should().BeTrue();
        options.Install.ProtocolUri.Should().Be("com.epicgames.launcher://store/product/epicnamespace/fortnite?action=install");
        options.Launch.IsSupported.Should().BeFalse();
        options.EpicCatalogNamespace.Should().Be("epicnamespace");
    }
}
