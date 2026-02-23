using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using Domain.Selection;
using FluentAssertions;
using Xunit;
using SteamBacklogPicker.UI.Services.GameArt;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Library;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.ViewModels;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class MainWindowPresentationTests
{
    [Fact]
    public async Task MainWindow_ShouldCoverOpenFilterDrawAndActionsJourneys()
    {
        var games = new[]
        {
            new GameEntry
            {
                Id = GameIdentifier.ForSteam(10),
                Title = "Installed game",
                InstallState = InstallState.Installed,
                Tags = new[] { "RPG" },
            },
            new GameEntry
            {
                Id = GameIdentifier.ForSteam(20),
                Title = "Not installed game",
                InstallState = InstallState.Available,
                Tags = new[] { "Action" },
            },
        };

        var localization = new LocalizationService();
        var launchService = new FakeGameLaunchService();
        ProcessStartInfo? startedProcess = null;

        var viewModel = new MainViewModel(
            new SelectionEngine(),
            new FakeLibraryService(games),
            new FakeArtLocator(),
            new FakeToastService(),
            localization,
            launchService,
            info =>
            {
                startedProcess = info;
                return null;
            });

        await viewModel.InitializeAsync();

        viewModel.DrawCommand.CanExecute(null).Should().BeTrue();
        viewModel.Preferences.RequireInstalled = true;

        viewModel.DrawCommand.Execute(null);
        await Task.Delay(1000);

        viewModel.SelectedGame.Title.Should().Be("Installed game");
        viewModel.SelectedGame.CanLaunch.Should().BeTrue();
        viewModel.SelectedGame.CanInstall.Should().BeFalse();

        viewModel.LaunchCommand.Execute(null);
        startedProcess.Should().NotBeNull();
        startedProcess!.FileName.Should().Be("steam://run/10");

        viewModel.Preferences.RequireInstalled = false;
        InvokeApplySelection(viewModel, games[1]);

        viewModel.InstallCommand.Execute(null);
        startedProcess.FileName.Should().Be("steam://install/20");
    }

    [Fact]
    public void MainWindowAxaml_ShouldBindCoreControlsToMainViewModel()
    {
        var axaml = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "src/Presentation/SteamBacklogPicker.Linux/Views/MainWindow.axaml"));

        axaml.Should().Contain("{Binding Preferences.RequireInstalled");
        axaml.Should().Contain("{Binding Preferences.ExcludeDeckUnsupported");
        axaml.Should().Contain("{Binding Preferences.CollectionOptions}");
        axaml.Should().Contain("{Binding Preferences.SelectedCollection, Mode=TwoWay}");
        axaml.Should().Contain("Command=\"{Binding RefreshCommand}\"");
        axaml.Should().Contain("Command=\"{Binding DrawCommand}\"");
        axaml.Should().Contain("Text=\"{Binding StatusMessage}\"");
        axaml.Should().Contain("Text=\"{Binding SelectedGame.Title}\"");
        axaml.Should().Contain("Text=\"{Binding SelectedGame.InstallationStatus}\"");
        axaml.Should().Contain("Command=\"{Binding LaunchCommand}\"");
        axaml.Should().Contain("Command=\"{Binding InstallCommand}\"");
        axaml.Should().Contain("Command=\"{Binding ChangeLanguageCommand}\"");
        axaml.Should().Contain("{DynamicResource Filters_DrawButton}");
        axaml.Should().Contain("{DynamicResource GameDetails_PlayButton}");
        axaml.Should().Contain("StringNullOrWhiteSpaceToBoolConverter");
        axaml.Should().Contain("IsVisible=\"{Binding SelectedGame.CoverImagePath, Converter={StaticResource StringNullOrWhiteSpaceToBoolConverter}}\"");
        axaml.Should().Contain("ConverterParameter=Invert");
    }

    private static void InvokeApplySelection(MainViewModel viewModel, GameEntry game)
    {
        var method = typeof(MainViewModel).GetMethod("ApplySelection", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(viewModel, new object[] { game });
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamBacklogPicker.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FakeLibraryService : IGameLibraryService
    {
        private readonly IReadOnlyList<GameEntry> _games;

        public FakeLibraryService(IReadOnlyList<GameEntry> games)
        {
            _games = games;
        }

        public Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_games);
        }
    }

    private sealed class FakeGameLaunchService : IGameLaunchService
    {
        public GameLaunchOptions GetLaunchOptions(GameEntry game)
        {
            var appId = game.Id.SteamAppId ?? 0;
            var launch = game.InstallState == InstallState.Installed
                ? GameLaunchAction.Supported($"steam://run/{appId}")
                : GameLaunchAction.Unsupported("Install first.");
            var install = game.InstallState == InstallState.Available
                ? GameLaunchAction.Supported($"steam://install/{appId}")
                : GameLaunchAction.Unsupported("Already installed.");

            return new GameLaunchOptions(launch, install);
        }
    }

    private sealed class FakeArtLocator : IGameArtLocator
    {
        public string? FindHeroImage(GameEntry game) => null;
    }

    private sealed class FakeToastService : IToastNotificationService
    {
        public void ShowGameSelected(GameEntry game, string? coverImagePath)
        {
        }
    }
}
