using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Localization;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class GameLaunchServiceTests
{
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
