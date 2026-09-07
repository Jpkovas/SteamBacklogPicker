using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using Domain.Selection;
using FluentAssertions;
using Xunit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SteamBacklogPicker.Linux.Controls;
using SteamBacklogPicker.Linux.Views;
using SteamBacklogPicker.UI.Services.GameArt;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Library;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.Services.Notifications;
using SteamBacklogPicker.UI.ViewModels;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class MainWindowPresentationTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "sbp-presentation-" + Guid.NewGuid().ToString("N"));

    public MainWindowPresentationTests() => Directory.CreateDirectory(_temporaryDirectory);

    public void Dispose() => Directory.Delete(_temporaryDirectory, recursive: true);

    private SelectionEngine CreateEngine() => new(Path.Combine(_temporaryDirectory, "settings.json"));

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

        using var viewModel = new MainViewModel(
            CreateEngine(),
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

        viewModel.LastRefreshError.Should().BeNull();
        viewModel.DrawCommand.CanExecute(null).Should().BeTrue();
        viewModel.Preferences.RequireInstalled = true;

        viewModel.DrawCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsDrawing);

        viewModel.SelectedGame.Title.Should().Be("Installed game");
        viewModel.SelectedGame.CanLaunch.Should().BeTrue();
        viewModel.SelectedGame.CanInstall.Should().BeFalse();

        viewModel.LaunchCommand.Execute(null);
        startedProcess.Should().NotBeNull();
        startedProcess!.FileName.Should().Be("steam://run/10");

        viewModel.Preferences.RequireInstalled = false;
        viewModel.SelectGameCommand.Execute(viewModel.VisibleGames.Single(card => card.Entry.Id == games[1].Id));

        viewModel.InstallCommand.Execute(null);
        startedProcess.FileName.Should().Be("steam://install/20");
    }

    [Fact]
    public async Task MainViewModel_ShouldUsePortugueseSingularAvailabilityStatus()
    {
        var games = new[]
        {
            new GameEntry
            {
                Id = GameIdentifier.ForSteam(10),
                Title = "Jogo",
                ProductCategory = ProductCategory.Game,
                InstallState = InstallState.Available,
            },
            new GameEntry
            {
                Id = GameIdentifier.ForSteam(20),
                Title = "Ferramenta",
                ProductCategory = ProductCategory.Tool,
                InstallState = InstallState.Available,
            },
        };
        var localization = new LocalizationService();
        localization.SetLanguage("pt-BR");

        using var viewModel = new MainViewModel(
            CreateEngine(),
            new FakeLibraryService(games),
            new FakeArtLocator(),
            new FakeToastService(),
            localization,
            new FakeGameLaunchService());

        await viewModel.InitializeAsync();

        viewModel.LastRefreshError.Should().BeNull();
        viewModel.EligibleGameCount.Should().Be(1);
        viewModel.TotalGameCount.Should().Be(2);
        viewModel.StatusMessage.Should().Be(localization.GetString("Status_FilteredCount_Singular", localization.FormatGameCount(1), localization.FormatGameCount(2)));
    }

    [AvaloniaFact]
    public async Task MainWindow_ShouldNavigateSearchAndCloseAdvancedFilters()
    {
        using var viewModel = CreateViewModel(new FakeLibraryService(SampleGames()));
        await viewModel.InitializeAsync();
        var window = new MainWindow(viewModel);
        window.Show();
        try
        {
            CaptureEvidence(window, "discover.png");
            window.FindControl<Control>("DiscoverPage")!.IsVisible.Should().BeTrue();
            window.FindControl<Control>("AdvancedFiltersPanel")!.IsEffectivelyVisible.Should().BeFalse();
            Execute(window, "LibraryNavigation");
            window.FindControl<Control>("LibraryPage")!.IsVisible.Should().BeTrue();
            window.FindControl<ListBox>("LibraryList")!.ItemCount.Should().Be(2);
            var libraryAction = window.FindControl<ListBox>("LibraryList")!.GetVisualDescendants()
                .OfType<Button>().First(button => button.Command == viewModel.SelectGameCommand);
            libraryAction.CommandParameter.Should().BeOfType<LibraryCardViewModel>();
            window.FindControl<TextBox>("SearchBox")!.Text = "missing title";
            Dispatcher.UIThread.RunJobs();
            viewModel.VisibleGames.Should().BeEmpty();
            viewModel.HasNoMatches.Should().BeTrue();
            Execute(window, "DiscoverNavigation");
            window.FindControl<Control>("HeroPanel")!.IsEffectivelyVisible.Should().BeFalse();
            window.FindControl<Control>("DiscoverEmptyState")!.IsEffectivelyVisible.Should().BeTrue();
            viewModel.ResetFiltersCommand.Execute(null);
            viewModel.AdvancedFilters = true;
            CaptureEvidence(window, "filters.png");
            window.FindControl<Control>("AdvancedFiltersPanel")!.IsEffectivelyVisible.Should().BeTrue();
            Execute(window, "SettingsNavigation");
            viewModel.AdvancedFilters.Should().BeFalse();
            window.FindControl<Control>("AdvancedFiltersPanel")!.IsEffectivelyVisible.Should().BeFalse();
            window.FindControl<Control>("FilterBar")!.IsEffectivelyVisible.Should().BeFalse();
            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
            viewModel.IsLibraryPage.Should().BeTrue();
            window.FindControl<TextBox>("SearchBox")!.IsFocused.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MainWindow_ShouldBindInstalledBacklogAndDrawHistoryActions()
    {
        using var viewModel = CreateViewModel(new FakeLibraryService(SampleGames()));
        await viewModel.InitializeAsync();
        var window = new MainWindow(viewModel);
        window.Show();
        try
        {
            window.FindControl<ToggleButton>("InstalledFilter")!.IsChecked = true;
            viewModel.VisibleGames.Should().ContainSingle(card => card.Entry.InstallState == InstallState.Installed);
            Execute(window, "DrawButton");
            await WaitUntilAsync(() => !viewModel.IsDrawing);
            viewModel.History.Should().ContainSingle();
            viewModel.HasSuggestions.Should().BeFalse();
            window.FindControl<Control>("SuggestionsHeading")!.IsEffectivelyVisible.Should().BeFalse();
            window.FindControl<Control>("SuggestionCards")!.IsEffectivelyVisible.Should().BeFalse();
            window.FindControl<Button>("DrawButton")!.IsEffectivelyVisible.Should().BeTrue();
            window.FindControl<Button>("LaunchButton")!.IsEffectivelyEnabled.Should().BeTrue();
            window.FindControl<Button>("InstallButton")!.IsEffectivelyEnabled.Should().BeFalse();
            window.FindControl<Button>("LaunchButton")!.IsEffectivelyVisible.Should().BeTrue();
            window.FindControl<Button>("InstallButton")!.IsEffectivelyVisible.Should().BeFalse();
            viewModel.SetBacklogStatusCommand.Execute("Completed");
            viewModel.VisibleGames.Should().BeEmpty();
            var filter = window.FindControl<ComboBox>("BacklogFilterBox")!;
            filter.SelectedItem = filter.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "completed"));
            viewModel.BacklogFilter.Should().Be("completed");
            viewModel.VisibleGames.Should().ContainSingle();
            Execute(window, "HistoryNavigation");
            window.FindControl<ListBox>("HistoryList")!.ItemCount.Should().Be(1);
            viewModel.ClearHistoryCommand.Execute(null);
            window.FindControl<ListBox>("HistoryList")!.ItemCount.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MainWindow_ShouldRenderAndClearEphemeralQrChallenge()
    {
        var library = new FakeSynchronizingLibrary();
        using var viewModel = CreateViewModel(library);
        await viewModel.InitializeAsync();
        var window = new MainWindow(viewModel);
        window.Show();
        try
        {
            Execute(window, "SettingsNavigation");
            library.SetChallenge("https://s.team/q/1/fixture-challenge");
            var image = window.FindControl<Image>("LoginQrImage")!;
            await WaitUntilAsync(() => image.Source is not null);
            image.Source.Should().BeOfType<Bitmap>();
            image.IsEffectivelyVisible.Should().BeTrue();
            var originalBitmap = image.Source;
            library.SetChallenge("https://s.team/q/1/fixture-challenge");
            Dispatcher.UIThread.RunJobs();
            image.Source.Should().BeSameAs(originalBitmap, "unrelated synchronization updates must not encode the same QR again");
            library.SetChallenge("https://s.team/q/1/" + new string('x', 3000));
            await WaitUntilAsync(() => image.Source is null);
            image.Source.Should().BeNull("oversized QR payloads must leave the connection UI usable");
            library.SetChallenge("https://s.team/q/1/replacement-challenge");
            await WaitUntilAsync(() => image.Source is not null);
            library.SetChallenge(null);
            await WaitUntilAsync(() => image.Source is null);
            image.Source.Should().BeNull();
            image.IsEffectivelyVisible.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CachedArtwork_ShouldLoadCurrentLocalImageAndReleaseOnDetach()
    {
        var path = Path.Combine(Path.GetTempPath(), "sbp-artwork-" + Guid.NewGuid().ToString("N") + ".png");
        var image = new CachedArtwork { AllowNetwork = false };
        var window = new Window { Content = image };
        try
        {
            using (var bitmap = new WriteableBitmap(new Avalonia.PixelSize(24, 8), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
                bitmap.Save(path);
            window.Show();
            image.SourcePath = path;
            await WaitUntilAsync(() => image.Source is not null);
            ((Bitmap)image.Source!).PixelSize.AspectRatio.Should().Be(3);
            window.Content = null;
            image.Source.Should().BeNull();
        }
        finally
        {
            window.Close();
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_ShouldKeepCompactFiltersInlineAndUseOneBacklogMenu()
    {
        var games = SampleGames();
        games[0] = games[0] with { OwnershipType = OwnershipType.Owned };
        games[1] = games[1] with { OwnershipType = OwnershipType.FamilyShared };
        using var viewModel = CreateViewModel(new FakeLibraryService(games));
        await viewModel.InitializeAsync();
        var window = new MainWindow(viewModel) { Width = 980, Height = 680 };
        window.Show();
        try
        {
            window.UpdateLayout();
            window.FindControl<Control>("NavigationSidebar")!.GetVisualDescendants().OfType<Button>().Should().HaveCount(4);
            foreach (var name in new[] { "SearchBox", "InstalledFilter", "FiltersToggle", "AllOwnershipButton", "OwnedOwnershipButton", "FamilyOwnershipButton" })
                window.FindControl<Control>(name)!.Bounds.Height.Should().Be(40);
            var segmentWidth = window.FindControl<Button>("AllOwnershipButton")!.Bounds.Width;
            window.FindControl<Button>("OwnedOwnershipButton")!.Bounds.Width.Should().Be(segmentWidth);
            window.FindControl<Button>("FamilyOwnershipButton")!.Bounds.Width.Should().Be(segmentWidth);
            window.FindControl<Button>("DrawButton")!.Bounds.Height.Should().Be(44);
            Execute(window, "OwnedOwnershipButton");
            viewModel.VisibleGames.Should().ContainSingle(card => card.Entry.OwnershipType == OwnershipType.Owned);
            window.FindControl<Button>("OwnedOwnershipButton")!.Classes.Should().Contain("active");
            Execute(window, "FamilyOwnershipButton");
            viewModel.VisibleGames.Should().ContainSingle(card => card.Entry.OwnershipType == OwnershipType.FamilyShared);
            Execute(window, "AllOwnershipButton");

            var badge = window.FindControl<Control>("AdvancedFilterBadge")!;
            badge.IsEffectivelyVisible.Should().BeFalse();
            viewModel.Preferences.IncludeTools = true;
            badge.IsEffectivelyVisible.Should().BeTrue();
            viewModel.AdvancedFilterCount.Should().Be(1);
            Execute(window, "ResetFiltersButton");
            badge.IsEffectivelyVisible.Should().BeFalse();

            window.FindControl<ToggleButton>("FiltersToggle")!.IsChecked = true;
            window.UpdateLayout();
            var filters = window.FindControl<Control>("AdvancedFiltersPanel")!;
            var page = window.FindControl<Control>("DiscoverPage")!;
            var filterBottom = filters.TranslatePoint(new Avalonia.Point(0, filters.Bounds.Height), window)!.Value.Y;
            var pageTop = page.TranslatePoint(default, window)!.Value.Y;
            filterBottom.Should().BeLessThanOrEqualTo(pageTop, "expanded filters occupy layout space instead of covering the page");
            window.FindControl<ComboBox>("CollectionFilterBox")!.Bounds.Height.Should().Be(40);
            window.FindControl<ComboBox>("BacklogFilterBox")!.Bounds.Height.Should().Be(40);
            var suggestionCards = window.FindControl<ItemsControl>("SuggestionCards")!.GetVisualDescendants()
                .OfType<Button>().Where(card => card.Classes.Contains("gameCard")).ToArray();
            suggestionCards.Should().HaveCount(2);
            suggestionCards[0].Bounds.Height.Should().Be(suggestionCards[1].Bounds.Height);
            suggestionCards[0].TranslatePoint(default, window)!.Value.Y.Should()
                .Be(suggestionCards[1].TranslatePoint(default, window)!.Value.Y);
            CaptureEvidence(window, "filters-minimum.png");
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            viewModel.AdvancedFilters.Should().BeFalse();
            viewModel.SelectGameCommand.Execute(viewModel.VisibleGames[0]);
            window.UpdateLayout();
            var button = window.FindControl<Button>("BacklogMenuButton")!;
            var menu = (MenuFlyout)button.Flyout!;
            menu.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            var actions = menu.Items.OfType<MenuItem>().ToArray();
            actions.Should().HaveCount(6);
            actions.Should().OnlyContain(item => item.Command == viewModel.SetBacklogStatusCommand);
            CaptureEvidence(window, "backlog-menu.png");
            var want = actions.Single(item => Equals(item.CommandParameter, "WantToPlay"));
            want.Command!.Execute(want.CommandParameter);
            menu.Hide();
            viewModel.SelectedBacklogLabel.Should().Be("Want to play");
            viewModel.CanUndoBacklog.Should().BeTrue();
            viewModel.UndoBacklogCommand.Execute(null);
            viewModel.CanUndoBacklog.Should().BeFalse();
            viewModel.SelectGameCommand.Execute(viewModel.VisibleGames.Single(card => card.Entry.InstallState == InstallState.Available));
            window.FindControl<Button>("LaunchButton")!.IsEffectivelyVisible.Should().BeFalse();
            window.FindControl<Button>("InstallButton")!.IsEffectivelyVisible.Should().BeTrue();
            window.FindControl<Button>("InstallButton")!.IsEffectivelyEnabled.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MotionStyles_ShouldUseShortFiniteTransitionsOnlyWhenEnabled()
    {
        var button = new Button { Classes = { "secondary" } };
        var window = new Window { Content = button, Classes = { "motion-enabled" } };
        window.Show();
        try
        {
            button.Transitions.Should().ContainSingle().Which.Should().BeOfType<DoubleTransition>()
                .Which.Duration.Should().Be(TimeSpan.FromMilliseconds(180));
            window.Classes.Remove("motion-enabled");
            button.Transitions.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MainWindow_ShouldVirtualizeLargeLibraryAndResolveOnlyVisibleArtwork()
    {
        var games = Enumerable.Range(1, 5000).Select(id => new GameEntry
        {
            Id = GameIdentifier.ForSteam((uint)id), Title = $"Game {id:D5}",
            InstallState = InstallState.Available, ProductCategory = ProductCategory.Game
        }).ToArray();
        var art = new FakeArtLocator();
        using var viewModel = new MainViewModel(CreateEngine(), new FakeLibraryService(games), art,
            new FakeToastService(), EnglishLocalization(), new FakeGameLaunchService());
        await viewModel.InitializeAsync();
        var window = new MainWindow(viewModel) { Width = 980, Height = 680 };
        window.Show();
        try
        {
            Execute(window, "LibraryNavigation");
            window.UpdateLayout();
            var list = window.FindControl<ListBox>("LibraryList")!;
            list.ItemCount.Should().Be(5000);
            list.GetVisualDescendants().OfType<ListBoxItem>().Count().Should().BeInRange(1, 30);
            art.RequestCount.Should().BeLessThan(40, "artwork discovery should follow visible rows, not the whole library");
            var realizedBeforeScroll = art.RequestCount;
            list.ScrollIntoView(viewModel.VisibleGames[^1]);
            window.UpdateLayout();
            list.GetVisualDescendants().OfType<ListBoxItem>().Count().Should().BeInRange(1, 30);
            art.RequestCount.Should().BeGreaterThan(realizedBeforeScroll);
            art.RequestCount.Should().BeLessThan(80);
        }
        finally { window.Close(); }
    }

    private MainViewModel CreateViewModel(IGameLibraryService library) => new(
        CreateEngine(), library, new FakeArtLocator(), new FakeToastService(),
        EnglishLocalization(), new FakeGameLaunchService());

    private static LocalizationService EnglishLocalization()
    {
        var localization = new LocalizationService();
        localization.SetLanguage("en-US");
        return localization;
    }

    private static GameEntry[] SampleGames() => new[]
    {
        new GameEntry { Id = GameIdentifier.ForSteam(10), Title = "Installed game", InstallState = InstallState.Installed, ProductCategory = ProductCategory.Game },
        new GameEntry { Id = GameIdentifier.ForSteam(20), Title = "Available game", InstallState = InstallState.Available, ProductCategory = ProductCategory.Game }
    };

    private static void Execute(Window window, string name)
    {
        var button = window.FindControl<Button>(name)!;
        button.Command.Should().NotBeNull();
        button.Command!.CanExecute(button.CommandParameter).Should().BeTrue();
        button.Command.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntilAsync(Func<bool> completed)
    {
        var timeout = Stopwatch.StartNew();
        while (!completed() && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        completed().Should().BeTrue("the asynchronous operation should complete within the test deadline");
    }

    private static void CaptureEvidence(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("SBP_UI_EVIDENCE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name));
    }

    private sealed class FakeSynchronizingLibrary : IGameLibraryService, ILibrarySynchronization
    {
        public event EventHandler<LibrarySnapshotEventArgs>? SnapshotChanged { add { } remove { } }
        public event EventHandler? StatusChanged;
        public LibrarySyncStatus Status { get; private set; } = new();
        public bool NetworkEnabled { get; set; }
        public string Language { get; set; } = "en-US";
        public Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GameEntry>>(SampleGames());
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void CancelSynchronization() { }
        public void SetChallenge(string? challenge)
        {
            Status = Status with { QrChallengeUrl = challenge, IsBusy = challenge is not null };
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
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
        public int RequestCount { get; private set; }
        public string? FindHeroImage(GameEntry game)
        {
            RequestCount++;
            return null;
        }
    }

    private sealed class FakeToastService : IToastNotificationService
    {
        public void ShowGameSelected(GameEntry game, string? coverImagePath)
        {
        }
    }
}
