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
    public void FromGame_UsesLaunchOptionsForSteamGame()
    {
        var localization = new FakeLocalizationService();
        var entry = new GameEntry
        {
            Id = GameIdentifier.ForSteam(440),
            Title = "Team Fortress 2",
            InstallState = InstallState.Installed,
        };

        var launchOptions = new GameLaunchOptions(
            GameLaunchAction.Supported("steam://run/440"),
            GameLaunchAction.Unsupported("Already installed."));

        var viewModel = GameDetailsViewModel.FromGame(entry, null, localization, launchOptions);

        viewModel.CanLaunch.Should().BeTrue();
        viewModel.CanInstall.Should().BeFalse();
        viewModel.LaunchUri.Should().Be("steam://run/440");
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
