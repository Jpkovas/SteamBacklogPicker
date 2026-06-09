using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.Tests.Fakes;
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

    [Fact]
    public void FromGame_ShouldExposeArtworkPlaceholder_WhenCoverIsMissing()
    {
        var localization = new FakeLocalizationService();
        var entry = new GameEntry
        {
            Id = GameIdentifier.ForSteam(440),
            Title = "Team Fortress 2",
            InstallState = InstallState.Installed,
        };

        var viewModel = GameDetailsViewModel.FromGame(entry, null, localization, GameLaunchOptions.Empty);

        viewModel.HasCoverImage.Should().BeFalse();
        viewModel.IsPlaceholder.Should().BeFalse();
        viewModel.ShowArtworkPlaceholder.Should().BeTrue();
        viewModel.ArtworkPlaceholderTitle.Should().Be("GameDetails_NoCoverTitle");
        viewModel.ArtworkPlaceholderSubtitle.Should().Be("GameDetails_NoCoverSubtitle");
    }

    [Fact]
    public void CreateEmpty_ShouldExposeDrawPromptArtworkPlaceholder()
    {
        var localization = new FakeLocalizationService();

        var viewModel = GameDetailsViewModel.CreateEmpty(localization);

        viewModel.HasCoverImage.Should().BeFalse();
        viewModel.IsPlaceholder.Should().BeTrue();
        viewModel.ShowArtworkPlaceholder.Should().BeTrue();
        viewModel.ArtworkPlaceholderTitle.Should().Be("GameDetails_NoSelectionTitle");
        viewModel.ArtworkPlaceholderSubtitle.Should().Be("GameDetails_DrawPrompt");
    }

    [Fact]
    public void FromGame_ShouldHideArtworkPlaceholder_WhenCoverExists()
    {
        var localization = new FakeLocalizationService();
        var entry = new GameEntry
        {
            Id = GameIdentifier.ForSteam(440),
            Title = "Team Fortress 2",
            InstallState = InstallState.Installed,
        };

        var viewModel = GameDetailsViewModel.FromGame(entry, "/tmp/cover.jpg", localization, GameLaunchOptions.Empty);

        viewModel.HasCoverImage.Should().BeTrue();
        viewModel.ShowArtworkPlaceholder.Should().BeFalse();
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
}
