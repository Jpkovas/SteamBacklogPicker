using System;
using System.Linq;
using System.Threading.Tasks;
using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services;
using SteamBacklogPicker.UI.ViewModels;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class GameDetailsViewModelTests
{
    [Fact]
    public async Task SaveUserDataCommand_ShouldPersistSanitizedData()
    {
        var game = new GameEntry
        {
            AppId = 99,
            Title = "Test Game",
            InstallState = InstallState.Installed,
            OwnershipType = OwnershipType.Owned,
            UserData = new GameUserData
            {
                Status = BacklogStatus.Unspecified,
            }
        };

        var service = new FakeUserDataService();
        var localization = new LocalizationService();
        var viewModel = GameDetailsViewModel.FromGame(game, null, localization, service);

        viewModel.PersonalNotes = "  Notes   ";
        viewModel.PlaytimeHoursText = "5.5";
        viewModel.TargetSessionHoursText = "2";
        viewModel.SelectedStatusOption = viewModel.StatusOptions.First(option => option.Status == BacklogStatus.Playing);

        viewModel.SaveUserDataCommand.Execute(null);
        var saved = await service.SavedCompletion.Task.ConfigureAwait(true);

        saved.Status.Should().Be(BacklogStatus.Playing);
        saved.Notes.Should().Be("Notes");
        saved.Playtime.Should().Be(TimeSpan.FromHours(5.5));
        saved.TargetSessionLength.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void RefreshLocalization_ShouldUpdateProgressMessages()
    {
        var game = new GameEntry
        {
            AppId = 15,
            Title = "Localized Game",
            InstallState = InstallState.Installed,
            OwnershipType = OwnershipType.Owned,
            UserData = new GameUserData
            {
                Status = BacklogStatus.Backlog,
                Playtime = TimeSpan.FromHours(3),
                EstimatedCompletionTime = TimeSpan.FromHours(6),
            }
        };

        var service = new FakeUserDataService();
        var localization = new LocalizationService();
        var viewModel = GameDetailsViewModel.FromGame(game, null, localization, service);

        viewModel.ProgressSummary.Should().Contain("3");

        localization.SetLanguage("en-US");
        viewModel.RefreshLocalization();

        viewModel.StatusOptions.Should().Contain(option => option.DisplayName == localization.GetString("BacklogStatus_Backlog"));
        viewModel.EstimatedCompletionText.Should().Contain("6");
    }

    private sealed class FakeUserDataService : IGameUserDataService
    {
        public TaskCompletionSource<GameUserData> SavedCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GameUserData> LoadAsync(GameEntry game, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(game.UserData);
        }

        public Task<GameUserData> SaveAsync(uint appId, GameUserData userData, System.Threading.CancellationToken cancellationToken = default)
        {
            SavedCompletion.TrySetResult(userData);
            return Task.FromResult(userData);
        }
    }
}
