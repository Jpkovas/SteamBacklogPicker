using Domain;
using EpicDiscovery;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Localization;
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

        var service = new GameLaunchService(CreateLocalization(), id => id.Equals(identifier) ? catalogItem : null);

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

        var service = new GameLaunchService(CreateLocalization(), _ => catalogItem);

        var options = service.GetLaunchOptions(entry);

        options.Install.IsSupported.Should().BeTrue();
        options.Install.ProtocolUri.Should().Be("com.epicgames.launcher://store/product/epicnamespace/fortnite?action=install");
        options.Launch.IsSupported.Should().BeFalse();
        options.EpicCatalogNamespace.Should().Be("epicnamespace");
    }

    [Theory]
    [InlineData("en-US", "Install the game before launching it.")]
    [InlineData("pt-BR", "Instale o jogo antes de executá-lo.")]
    public void GetLaunchOptions_LocalizesLaunchErrors(string languageCode, string expectedMessage)
    {
        var localization = CreateLocalization(languageCode);
        var service = new GameLaunchService(localization);

        var entry = new GameEntry
        {
            Id = GameIdentifier.ForSteam(440),
            Title = "Team Fortress 2",
            InstallState = InstallState.Available,
        };

        var options = service.GetLaunchOptions(entry);

        options.Launch.IsSupported.Should().BeFalse();
        options.Launch.ErrorMessage.Should().Be(expectedMessage);
    }

    private static LocalizationService CreateLocalization(string? language = null)
    {
        var localization = new LocalizationService();
        if (!string.IsNullOrWhiteSpace(language))
        {
            localization.SetLanguage(language);
        }
        return localization;
    }
}
