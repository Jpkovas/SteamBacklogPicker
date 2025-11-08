using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services;
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
