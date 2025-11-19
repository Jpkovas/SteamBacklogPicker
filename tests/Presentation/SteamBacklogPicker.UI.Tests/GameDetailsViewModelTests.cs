using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.ViewModels;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class GameDetailsViewModelTests
{
    [Fact]
    public void FromGame_UsesLaunchOptionsForEpicGame()
    {
        var localization = new FakeLocalizationService();
        var identifier = new GameIdentifier
        {
            Storefront = Storefront.EpicGamesStore,
            StoreSpecificId = "namespace:catalog",
        };
        var entry = new GameEntry
        {
            Id = identifier,
            Title = "Test Epic Game",
            InstallState = InstallState.Installed,
        };

        var launchOptions = new GameLaunchOptions(
            GameLaunchAction.Supported("com.epicgames.launcher://apps/TestApp?action=launch&silent=true"),
            GameLaunchAction.Unsupported("Install via Epic."),
            "TestApp",
            "catalog",
            "namespace");

        var viewModel = GameDetailsViewModel.FromGame(entry, null, localization, launchOptions);

        viewModel.CanLaunch.Should().BeTrue();
        viewModel.CanInstall.Should().BeFalse();
        viewModel.LaunchUri.Should().Be("com.epicgames.launcher://apps/TestApp?action=launch&silent=true");
        viewModel.EpicAppName.Should().Be("TestApp");
        viewModel.EpicCatalogItemId.Should().Be("catalog");
    }

    [Theory]
    [InlineData("en-US", "Install the game before launching it.")]
    [InlineData("pt-BR", "Instale o jogo antes de executá-lo.")]
    public void LaunchErrorMessage_IsLocalized(string languageCode, string expectedMessage)
    {
        var localization = new LocalizationService();
        localization.SetLanguage(languageCode);

        var entry = new GameEntry
        {
            Id = GameIdentifier.ForSteam(570),
            Title = "Dota 2",
            InstallState = InstallState.Available,
        };

        var launchService = new GameLaunchService(localization);
        var launchOptions = launchService.GetLaunchOptions(entry);

        var viewModel = GameDetailsViewModel.FromGame(entry, null, localization, launchOptions);

        viewModel.LaunchErrorMessage.Should().Be(expectedMessage);
        viewModel.CanLaunch.Should().BeFalse();
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public event EventHandler? LanguageChanged
        {
            add { }
            remove { }
        }

        public string CurrentLanguage => "en";

        public IReadOnlyList<string> SupportedLanguages => new[] { "en" };

        public void SetLanguage(string languageCode)
        {
        }

        public string GetString(string key)
        {
            return key;
        }

        public string GetString(string key, params object[] arguments)
        {
            return string.Format(key, arguments);
        }

        public string FormatGameCount(int count) => count.ToString();
    }
}
