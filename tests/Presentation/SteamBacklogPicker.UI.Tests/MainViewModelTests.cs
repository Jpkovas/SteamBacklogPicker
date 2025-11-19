using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using Domain.Selection;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.GameArt;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Library;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.ViewModels;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void LaunchGame_WhenLaunchUnsupported_SetsStatusMessage()
    {
        var launchOptions = new GameLaunchOptions(
            GameLaunchAction.Unsupported("Epic metadata is missing."),
            GameLaunchAction.Unsupported(),
            null,
            null,
            null);
        var launchService = new FakeGameLaunchService(_ => launchOptions);
        var localization = new FakeLocalizationService();
        ProcessStartInfo? startedInfo = null;
        Func<ProcessStartInfo, Process?> starter = info =>
        {
            startedInfo = info;
            return null;
        };

        var viewModel = CreateMainViewModel(launchService, localization, starter);
        var game = CreateEpicGame(InstallState.Installed);
        InvokeApplySelection(viewModel, game);

        viewModel.LaunchCommand.Execute(null);

        viewModel.StatusMessage.Should().Be("Epic metadata is missing.");
        startedInfo.Should().BeNull();
    }

    [Fact]
    public void InstallGame_WhenSupported_InvokesProcessStarter()
    {
        var installAction = GameLaunchAction.Supported("com.epicgames.launcher://store/product/namespace/catalog?action=install");
        var launchOptions = new GameLaunchOptions(
            GameLaunchAction.Unsupported(),
            installAction,
            "TestApp",
            "catalog",
            "namespace");
        var launchService = new FakeGameLaunchService(_ => launchOptions);
        var localization = new FakeLocalizationService();
        ProcessStartInfo? startedInfo = null;
        Func<ProcessStartInfo, Process?> starter = info =>
        {
            startedInfo = info;
            return null;
        };

        var viewModel = CreateMainViewModel(launchService, localization, starter);
        var game = CreateEpicGame(InstallState.Available);
        InvokeApplySelection(viewModel, game);

        viewModel.InstallCommand.Execute(null);

        startedInfo.Should().NotBeNull();
        startedInfo!.FileName.Should().Be("com.epicgames.launcher://store/product/namespace/catalog?action=install");
        viewModel.StatusMessage.Should().Be("Status_LoadingLibrary");
    }

    private static MainViewModel CreateMainViewModel(
        IGameLaunchService launchService,
        ILocalizationService localizationService,
        Func<ProcessStartInfo, Process?>? processStarter = null)
    {
        return new MainViewModel(
            new FakeSelectionEngine(),
            new FakeGameLibraryService(Array.Empty<GameEntry>()),
            new FakeGameArtLocator(),
            new FakeToastNotificationService(),
            localizationService,
            launchService,
            processStarter);
    }

    private static void InvokeApplySelection(MainViewModel viewModel, GameEntry game)
    {
        var method = typeof(MainViewModel).GetMethod("ApplySelection", BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(viewModel, new object[] { game });
    }

    private static GameEntry CreateEpicGame(InstallState state)
    {
        return new GameEntry
        {
            Id = new GameIdentifier
            {
                Storefront = Storefront.EpicGamesStore,
                StoreSpecificId = "namespace:catalog",
            },
            Title = "Test Game",
            InstallState = state,
        };
    }

    private sealed class FakeSelectionEngine : ISelectionEngine
    {
        private SelectionPreferences preferences = new();

        public SelectionPreferences GetPreferences() => preferences.Clone();

        public void UpdatePreferences(SelectionPreferences preferences) => this.preferences = preferences.Clone();

        public IReadOnlyList<SelectionHistoryEntry> GetHistory() => Array.Empty<SelectionHistoryEntry>();

        public void ClearHistory()
        {
        }

        public GameEntry PickNext(IEnumerable<GameEntry> games) => games.First();

        public IReadOnlyList<GameEntry> FilterGames(IEnumerable<GameEntry> games) => games.ToList();
    }

    private sealed class FakeGameLibraryService : IGameLibraryService
    {
        private readonly IReadOnlyList<GameEntry> games;

        public FakeGameLibraryService(IReadOnlyList<GameEntry> games)
        {
            this.games = games;
        }

        public Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(games);
        }
    }

    private sealed class FakeGameArtLocator : IGameArtLocator
    {
        public string? FindHeroImage(GameEntry game) => null;
    }

    private sealed class FakeToastNotificationService : IToastNotificationService
    {
        public void ShowGameSelected(GameEntry game, string? coverImagePath)
        {
        }
    }

    private sealed class FakeGameLaunchService : IGameLaunchService
    {
        private readonly Func<GameEntry, GameLaunchOptions> factory;

        public FakeGameLaunchService(Func<GameEntry, GameLaunchOptions> factory)
        {
            this.factory = factory;
        }

        public GameLaunchOptions GetLaunchOptions(GameEntry game) => factory(game);
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

        public string GetString(string key) => key;

        public string GetString(string key, params object[] arguments) => string.Format(key, arguments);

        public string FormatGameCount(int count) => count.ToString();
    }
}
