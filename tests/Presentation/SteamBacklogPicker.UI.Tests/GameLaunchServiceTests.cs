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

    [Theory]
    [InlineData(InstallState.Installed, true, false)]
    [InlineData(InstallState.Available, false, true)]
    [InlineData(InstallState.Unknown, false, true)]
    [InlineData((InstallState)3, false, true)]
    public void GetLaunchOptions_ShouldUseInstallationEvidenceIndependentlyOfFamilyOwnership(InstallState state, bool canLaunch, bool canInstall)
    {
        var service = new GameLaunchService(CreateLocalization("en-US"));
        var entry = new GameEntry
        {
            Id = GameIdentifier.ForSteam(440),
            Title = "Family game",
            OwnershipType = OwnershipType.FamilyShared,
            InstallState = state
        };
        var options = service.GetLaunchOptions(entry);
        options.Launch.IsSupported.Should().Be(canLaunch);
        options.Install.IsSupported.Should().Be(canInstall);
    }

    [Fact]
    public void GetLaunchOptions_ShouldRejectZeroAppId()
    {
        var service = new GameLaunchService(CreateLocalization());
        var entry = new GameEntry { Id = GameIdentifier.ForSteam(0), InstallState = InstallState.Installed };
        var options = service.GetLaunchOptions(entry);
        options.Launch.IsSupported.Should().BeFalse();
        options.Install.IsSupported.Should().BeFalse();
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
