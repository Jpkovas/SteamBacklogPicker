using System.IO;
using System.Diagnostics;
using System.Collections.Specialized;
using Xunit.Abstractions;
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

public sealed class MainViewModelWorkspaceTests
{
    private readonly ITestOutputHelper _output;
    public MainViewModelWorkspaceTests(ITestOutputHelper output) => _output = output;

    private const string AccountA = "76561198000000000";
    private const string AccountB = "76561198000000001";

    [Fact]
    public async Task SearchAndOwnership_ShouldFilterByTitleOrIdWithoutPromotingUnknownOwnership()
    {
        using var fixture = new Fixture(new[]
        {
            Game(10, "Alpha", OwnershipType.Owned),
            Game(20, "Family Quest", OwnershipType.FamilyShared),
            Game(30, "Alpha cached", OwnershipType.Unknown)
        });
        var vm = fixture.ViewModel;
        fixture.Library.QueryCount.Should().Be(0);
        await vm.InitializeAsync();
        await vm.InitializeAsync();
        fixture.Library.QueryCount.Should().Be(1);
        vm.SearchText = "ALPHA";
        vm.VisibleGames.Select(card => card.AppId).Should().BeEquivalentTo(new uint?[] { 10, 30 });
        vm.OwnershipFilter = "owned";
        vm.VisibleGames.Should().ContainSingle().Which.AppId.Should().Be(10);
        vm.SearchText = "20";
        vm.OwnershipFilter = "family";
        vm.VisibleGames.Should().ContainSingle().Which.AppId.Should().Be(20);
        vm.SearchText = "missing";
        vm.HasNoMatches.Should().BeTrue();
        vm.DrawCommand.CanExecute(null).Should().BeFalse();
        vm.ResetFiltersCommand.Execute(null);
        vm.VisibleGames.Should().HaveCount(3);
    }

    [Fact]
    public async Task BacklogAndUndo_ShouldUpdateEligibilityWithoutRecordingPreviewsAsDraws()
    {
        using var fixture = new Fixture(new[] { Game(10, "Alpha"), Game(20, "Beta") });
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        vm.History.Should().BeEmpty();
        vm.SelectGameCommand.Execute(vm.VisibleGames.Single(card => card.AppId == 20));
        vm.SetBacklogStatusCommand.Execute("Completed");
        fixture.Backlog.GetStatus("local", 20).Should().Be(BacklogStatus.Completed);
        vm.VisibleGames.Select(card => card.AppId).Should().Equal(10u);
        vm.CanUndoBacklog.Should().BeTrue();
        vm.BacklogFilter = "completed";
        vm.VisibleGames.Should().ContainSingle().Which.AppId.Should().Be(20);
        vm.UndoBacklogCommand.Execute(null);
        fixture.Backlog.GetStatus("local", 20).Should().Be(BacklogStatus.None);
        vm.CanUndoBacklog.Should().BeFalse();
        vm.VisibleGames.Should().BeEmpty();
        vm.BacklogFilter = "active";
        vm.VisibleGames.Should().HaveCount(2);
        vm.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Draw_ShouldRestartAntiRepeatCycleOverOnlyEligibleInstalledGames()
    {
        using var fixture = new Fixture(new[]
        {
            Game(10, "Alpha"), Game(20, "Beta", OwnershipType.FamilyShared),
            Game(30, "Unavailable", OwnershipType.Owned, InstallState.Available)
        });
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        vm.Preferences.RequireInstalled = true;
        vm.VisibleGames.Should().HaveCount(2);
        await ExecuteAsync(vm.DrawCommand);
        await ExecuteAsync(vm.DrawCommand);
        vm.History.Select(row => row.AppId).Should().OnlyHaveUniqueItems();
        await ExecuteAsync(vm.DrawCommand);
        vm.History.Should().HaveCount(3);
        vm.History.Select(row => row.AppId).Should().OnlyContain(id => id == 10 || id == 20);
        vm.AvoidRepeats = false;
        vm.SearchText = "Alpha";
        await ExecuteAsync(vm.DrawCommand);
        await ExecuteAsync(vm.DrawCommand);
        vm.History.Take(2).Select(row => row.AppId).Should().Equal(10u, 10u);
        fixture.Backlog.GetAvoidRepeats("local").Should().BeFalse();
        vm.ClearHistoryCommand.Execute(null);
        vm.History.Should().BeEmpty();
        fixture.Backlog.GetHistory("local").Should().BeEmpty();
    }

    [Fact]
    public async Task FailedRefresh_ShouldKeepPreviousLibrarySelectionAndHistory()
    {
        using var fixture = new Fixture(new[] { Game(10, "Preserved") });
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        await ExecuteAsync(vm.DrawCommand);
        fixture.Library.Error = new IOException("temporary failure");
        await ExecuteAsync(vm.RefreshCommand);
        vm.VisibleGames.Should().ContainSingle().Which.Title.Should().Be("Preserved");
        vm.SelectedGame.Title.Should().Be("Preserved");
        vm.History.Should().ContainSingle();
        vm.LastRefreshError.Should().Be("temporary failure");
        vm.IsRefreshing.Should().BeFalse();
        vm.DrawCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task AccountSwitch_ShouldIsolateHistoryPreferencesAndUndoAndIgnoreStaleEvents()
    {
        var library = new SynchronizedLibrary(new[] { Game(10, "Account A") }, AccountA);
        var backlog = new BacklogStore();
        backlog.SetAvoidRepeats(AccountA, false);
        using var fixture = new Fixture(library, backlog);
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        await WaitUntilAsync(() => vm.VisibleGames.Count == 1);
        vm.AvoidRepeats.Should().BeFalse();
        await ExecuteAsync(vm.DrawCommand);
        vm.SetBacklogStatusCommand.Execute("Playing");
        vm.CanUndoBacklog.Should().BeTrue();
        library.Publish(new[] { Game(20, "Account B") }, AccountB, 2);
        await WaitUntilAsync(() => vm.VisibleGames.FirstOrDefault()?.AppId == 20 && vm.History.Count == 0);
        vm.CanUndoBacklog.Should().BeFalse();
        vm.History.Should().BeEmpty();
        vm.AvoidRepeats.Should().BeTrue();
        backlog.GetStatus(AccountA, 10).Should().Be(BacklogStatus.Playing);
        await ExecuteAsync(vm.DrawCommand);
        library.Publish(new[] { Game(90, "Stale generation") }, AccountA, 1, updateStatus: false);
        library.Publish(new[] { Game(91, "Wrong account") }, AccountA, 2, updateStatus: false);
        await Task.Delay(30);
        vm.VisibleGames.Should().ContainSingle().Which.AppId.Should().Be(20);
        library.Publish(new[] { Game(10, "Account A") }, AccountA, 3);
        // Cards are published before history within the UI callback; wait for the complete snapshot.
        await WaitUntilAsync(() => vm.VisibleGames.FirstOrDefault()?.AppId == 10
            && vm.History.Count == 1 && vm.History.FirstOrDefault()?.AppId == 10);
        vm.History.Should().ContainSingle().Which.AppId.Should().Be(10);
        vm.AvoidRepeats.Should().BeFalse();
        backlog.GetHistory(AccountB).Should().ContainSingle().Which.AppId.Should().Be(20);
    }

    [Fact]
    public async Task DelayedRefreshReturn_ShouldNotOverwriteSnapshotOfNewAccount()
    {
        var library = new SynchronizedLibrary(new[] { Game(10, "Account A") }, AccountA);
        using var fixture = new Fixture(library, new BacklogStore());
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        await WaitUntilAsync(() => vm.VisibleGames.Count == 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<IReadOnlyList<GameEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        library.OverrideLoad = _ => { started.TrySetResult(); return delayed.Task; };
        var refresh = ExecuteAsync(vm.RefreshCommand);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        library.Publish(new[] { Game(20, "New account") }, AccountB, 2);
        await WaitUntilAsync(() => vm.VisibleGames.FirstOrDefault()?.AppId == 20);
        delayed.SetResult(new[] { Game(10, "Old result") });
        await refresh;
        vm.VisibleGames.Should().ContainSingle().Which.Title.Should().Be("New account");
        vm.AccountSummary.Should().Contain(AccountB);
    }

    [Fact]
    public async Task OfflineDraw_ShouldNotPassRemoteArtworkToSystemNotifications()
    {
        var library = new SynchronizedLibrary(new[] { Game(10, "Example") }, AccountA);
        using var fixture = new Fixture(library, new BacklogStore());
        fixture.Art.Path = "https://cdn.cloudflare.steamstatic.com/steam/apps/10/library_hero.jpg";
        await fixture.ViewModel.InitializeAsync();
        await WaitUntilAsync(() => fixture.ViewModel.VisibleGames.Count == 1);
        await ExecuteAsync(fixture.ViewModel.DrawCommand);
        fixture.Toast.ImagePath.Should().BeNull();
        fixture.ViewModel.NetworkEnabled = true;
        await ExecuteAsync(fixture.ViewModel.DrawCommand);
        fixture.Toast.ImagePath.Should().Be(fixture.Art.Path);
    }

    [Fact]
    public async Task UnchangedRefresh_ShouldRestoreSummaryInsteadOfLeavingLoadingStatus()
    {
        using var fixture = new Fixture(new[] { Game(10, "Same game") });
        await fixture.ViewModel.InitializeAsync();
        var summary = fixture.ViewModel.StatusMessage;
        await ExecuteAsync(fixture.ViewModel.RefreshCommand);
        fixture.ViewModel.StatusMessage.Should().Be(summary);
    }

    [Fact]
    public async Task PendingDraw_ShouldNotPublishOldAccountSelectionAndShouldLeaveDispatcherFree()
    {
        var library = new SynchronizedLibrary(new[] { Game(10, "Old account") }, AccountA);
        using var fixture = new Fixture(library, new BacklogStore());
        await fixture.ViewModel.InitializeAsync();
        await WaitUntilAsync(() => fixture.ViewModel.VisibleGames.Count == 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        fixture.Engine.BeforePick = () => { started.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(); };
        var draw = ExecuteAsync(fixture.ViewModel.DrawCommand);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            library.Publish(new[] { Game(20, "New account") }, AccountB, 2);
            await WaitUntilAsync(() => fixture.ViewModel.SelectedGame.Title == "New account");
        }
        finally { release.Set(); }
        await draw;
        fixture.ViewModel.SelectedGame.Title.Should().Be("New account");
        fixture.ViewModel.History.Should().BeEmpty();
        fixture.Backlog.GetHistory(AccountB).Should().BeEmpty();
        fixture.Backlog.GetHistory(AccountA).Should().ContainSingle().Which.AppId.Should().Be(10);
    }

    [Fact]
    public async Task AdvancedFilterCount_ShouldCountEffectiveGroupsAndReset()
    {
        using var fixture = new Fixture(new[] { Game(10, "Test") with { Tags = new[] { "Favorites" } } });
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        vm.AdvancedFilterCount.Should().Be(0);
        vm.HasAdvancedFilterCount.Should().BeFalse();
        vm.Preferences.RequireInstalled = true;
        vm.OwnershipFilter = "owned";
        vm.BacklogFilter = "all";
        vm.AdvancedFilterCount.Should().Be(0);
        vm.Preferences.SelectedCollection = "Favorites";
        vm.Preferences.ExcludeDeckUnsupported = true;
        vm.Preferences.RequireMacCompatible = true;
        vm.Preferences.IncludeOther = true;
        vm.Preferences.IncludeSoundtracks = true;
        vm.AdvancedFilterCount.Should().Be(4, "categories count as one group");
        vm.HasAdvancedFilterCount.Should().BeTrue();
        vm.ChangeLanguageCommand.Execute("pt-BR");
        vm.AdvancedFilterCount.Should().Be(4);
        var changes = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.AdvancedFilterCount)) changes++; };
        vm.ResetFiltersCommand.Execute(null);
        vm.AdvancedFilterCount.Should().Be(0);
        vm.HasAdvancedFilterCount.Should().BeFalse();
        changes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Filtering_ShouldReuseCardsAndResolveArtworkOnlyOnDemand()
    {
        using var fixture = new Fixture(new[] { Game(10, "Alpha"), Game(20, "Beta") });
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        var original = vm.VisibleGames.ToArray();
        fixture.Art.Calls.Should().Be(1, "only the selected hero needs an artwork path before controls bind");
        var notifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.VisibleGames)) notifications++; };
        vm.SearchText = "a";
        notifications.Should().Be(0, "equivalent filtered contents should keep the ItemsSource");
        vm.SearchText = "Alpha";
        vm.SearchText = "";
        vm.VisibleGames[0].Should().BeSameAs(original[0]);
        vm.VisibleGames[1].Should().BeSameAs(original[1]);
        fixture.Art.Calls.Should().Be(1);
        _ = vm.VisibleGames[1].CoverImagePath;
        fixture.Art.Calls.Should().Be(2);
        await ExecuteAsync(vm.RefreshCommand);
        notifications.Should().Be(2, "an unchanged snapshot should not rebuild the list");
        fixture.Art.Calls.Should().Be(2);
    }

    [Fact]
    public async Task History_ShouldUseAtMostTwoCollectionChangesPerDrawAtCapacity()
    {
        var backlog = new BacklogStore();
        for (uint id = 1; id <= BacklogStore.HistoryLimit; id++) backlog.RecordDraw("local", Game(id, "Old " + id));
        using var fixture = new Fixture(new Library(new[] { Game(999, "New draw") }), backlog);
        await fixture.ViewModel.InitializeAsync();
        var changes = new List<NotifyCollectionChangedAction>();
        fixture.ViewModel.History.CollectionChanged += (_, e) => changes.Add(e.Action);
        await ExecuteAsync(fixture.ViewModel.DrawCommand);
        fixture.ViewModel.History.Should().HaveCount(200);
        fixture.ViewModel.History[0].AppId.Should().Be(999);
        changes.Should().Equal(NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add);
    }

    [Fact]
    public async Task OldCard_ShouldNotSelectOrChangeBacklogAfterAccountSwitch()
    {
        var library = new SynchronizedLibrary(new[] { Game(10, "Old account") }, AccountA);
        using var fixture = new Fixture(library, new BacklogStore());
        await fixture.ViewModel.InitializeAsync();
        await WaitUntilAsync(() => fixture.ViewModel.VisibleGames.Count == 1);
        var oldCard = fixture.ViewModel.VisibleGames[0];
        library.Publish(new[] { Game(20, "New account") }, AccountB, 2);
        await WaitUntilAsync(() => fixture.ViewModel.SelectedGame.Title == "New account");
        fixture.ViewModel.SelectGameCommand.Execute(oldCard);
        fixture.ViewModel.SetBacklogStatusCommand.Execute("Playing");
        fixture.Backlog.GetStatus(AccountB, 10).Should().Be(BacklogStatus.None);
        fixture.Backlog.GetStatus(AccountB, 20).Should().Be(BacklogStatus.Playing);
        fixture.ViewModel.SelectedGame.Title.Should().Be("New account");
    }

    [Fact]
    public async Task FailedBacklogSave_ShouldNotOfferUndoOrAcceptUndefinedStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BacklogWorkspaceFailure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var fixture = new Fixture(new Library(new[] { Game(10, "Test") }), new BacklogStore(directory));
            await fixture.ViewModel.InitializeAsync();
            fixture.ViewModel.SetBacklogStatusCommand.Execute("Playing");
            fixture.ViewModel.CanUndoBacklog.Should().BeFalse();
            fixture.Backlog.GetStatus("local", 10).Should().Be(BacklogStatus.None);
            fixture.ViewModel.SetBacklogStatusCommand.Execute("9000");
            fixture.ViewModel.CanUndoBacklog.Should().BeFalse();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task DelayedRefresh_ShouldNotPublishAfterDisposal()
    {
        using var fixture = new Fixture(new[] { Game(10, "Before") });
        await fixture.ViewModel.InitializeAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<IReadOnlyList<GameEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Library.OverrideLoad = _ => { started.TrySetResult(); return delayed.Task; };
        var refresh = ExecuteAsync(fixture.ViewModel.RefreshCommand);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.ViewModel.Dispose();
        var notifications = 0;
        fixture.ViewModel.PropertyChanged += (_, _) => notifications++;
        delayed.SetResult(new[] { Game(20, "After disposal") });
        await refresh;
        fixture.ViewModel.VisibleGames.Should().ContainSingle().Which.AppId.Should().Be(10);
        notifications.Should().Be(0);
    }

    [Fact]
    public async Task Workspace_ShouldReportSyntheticLibraryPerformance()
    {
        var games = Enumerable.Range(1, 1930).Select(id => Game((uint)id, $"Game {id:D4}",
            id <= 1266 ? OwnershipType.FamilyShared : OwnershipType.Owned,
            id <= 14 ? InstallState.Installed : InstallState.Available)).ToArray();
        using var fixture = new Fixture(games);
        await fixture.ViewModel.InitializeAsync();
        var initialLookups = fixture.Art.Calls;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 40; i++) fixture.ViewModel.SearchText = i % 2 == 0 ? "Game 1" : "";
        watch.Stop();
        var searchBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var searchLookups = fixture.Art.Calls - initialLookups;
        fixture.ViewModel.VisibleGames.Should().HaveCount(1930);
        searchLookups.Should().Be(0);
        _output.WriteLine($"games=1930; family=1266; installed=14; search_updates=40; elapsed_ms={watch.Elapsed.TotalMilliseconds:F3}; allocated_bytes={searchBytes}; initial_art_lookups={initialLookups}; search_art_lookups={searchLookups}");
    }

    private static async Task ExecuteAsync(AsyncRelayCommand command)
    {
        command.CanExecute(null).Should().BeTrue();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false;
        void Changed(object? sender, EventArgs args)
        {
            if (!command.CanExecute(null)) started = true;
            else if (started) completed.TrySetResult();
        }
        command.CanExecuteChanged += Changed;
        try
        {
            command.Execute(null);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { command.CanExecuteChanged -= Changed; }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(10);
        condition().Should().BeTrue();
    }

    private static GameEntry Game(uint appId, string title, OwnershipType ownership = OwnershipType.Owned,
        InstallState installation = InstallState.Installed) => new()
    {
        Id = GameIdentifier.ForSteam(appId), Title = title, OwnershipType = ownership,
        InstallState = installation, ProductCategory = ProductCategory.Game
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MainViewModelWorkspaceTests", Guid.NewGuid().ToString("N"));
        public Library Library { get; }
        public BacklogStore Backlog { get; }
        public Artwork Art { get; } = new();
        public Notifications Toast { get; } = new();
        public InterceptingEngine Engine { get; }
        public MainViewModel ViewModel { get; }

        public Fixture(IReadOnlyList<GameEntry> games) : this(new Library(games), new BacklogStore()) { }

        public Fixture(Library library, BacklogStore backlog)
        {
            Directory.CreateDirectory(_directory);
            Library = library;
            Backlog = backlog;
            var engine = new SelectionEngine(Path.Combine(_directory, "selection.json"));
            engine.UpdatePreferences(new SelectionPreferences { Seed = 1, RecentGameExclusionCount = 0, HistoryLimit = 200 });
            Engine = new InterceptingEngine(engine);
            var localization = new LocalizationService();
            ViewModel = new MainViewModel(Engine, library, Art, Toast, localization,
                new GameLaunchService(localization), _ => null, backlog);
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private class Library(IReadOnlyList<GameEntry> games) : IGameLibraryService
    {
        public IReadOnlyList<GameEntry> Games { get; set; } = games;
        public Exception? Error { get; set; }
        public Func<CancellationToken, Task<IReadOnlyList<GameEntry>>>? OverrideLoad { get; set; }
        public int QueryCount { get; protected set; }
        public virtual Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
        {
            QueryCount++;
            if (OverrideLoad is not null) return OverrideLoad(cancellationToken);
            if (Error is not null) throw Error;
            return Task.FromResult(Games);
        }
    }

    private sealed class SynchronizedLibrary(IReadOnlyList<GameEntry> games, string account) : Library(games), ILibrarySynchronization
    {
        public event EventHandler<LibrarySnapshotEventArgs>? SnapshotChanged;
        public event EventHandler? StatusChanged;
        public LibrarySyncStatus Status { get; private set; } = new(account, Generation: 1);
        public bool NetworkEnabled { get; set; }
        public string Language { get; set; } = "en-US";

        public override async Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
        {
            if (OverrideLoad is not null) return await OverrideLoad(cancellationToken);
            var result = await base.GetLibraryAsync(cancellationToken);
            Publish(result, Status.AccountId, Status.Generation);
            return result;
        }

        public void Publish(IReadOnlyList<GameEntry> games, string accountId, int generation, bool updateStatus = true)
        {
            if (updateStatus)
            {
                Games = games;
                Status = Status with { AccountId = accountId, Generation = generation };
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            SnapshotChanged?.Invoke(this, new(games, accountId, generation));
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void CancelSynchronization() { }
    }

    private sealed class Artwork : IGameArtLocator
    {
        public int Calls { get; private set; }
        public string? Path { get; set; }
        public string? FindHeroImage(GameEntry game) { Calls++; return Path; }
    }

    private sealed class Notifications : IToastNotificationService
    {
        public string? ImagePath { get; private set; }
        public void ShowGameSelected(GameEntry game, string? coverImagePath) { ImagePath = coverImagePath; }
    }

    private sealed class InterceptingEngine(ISelectionEngine inner) : ISelectionEngine
    {
        public Action? BeforePick { get; set; }
        public SelectionPreferences GetPreferences() => inner.GetPreferences();
        public void UpdatePreferences(SelectionPreferences preferences) => inner.UpdatePreferences(preferences);
        public IReadOnlyList<SelectionHistoryEntry> GetHistory() => inner.GetHistory();
        public void ClearHistory() => inner.ClearHistory();
        public IReadOnlyList<GameEntry> FilterGames(IEnumerable<GameEntry> games) => inner.FilterGames(games);
        public GameEntry PickNext(IEnumerable<GameEntry> games) { BeforePick?.Invoke(); return inner.PickNext(games); }
    }
}
