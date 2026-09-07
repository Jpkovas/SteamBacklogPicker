using System.Collections.ObjectModel;
using System.Threading;
using Domain;
using SteamBacklogPicker.UI.Services.Library;
using SteamBacklogPicker.UI.Services.Localization;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed record LibraryCardViewModel
{
    private readonly Func<string?> _resolveCover;
    public LibraryCardViewModel(GameEntry entry, string? coverImagePath, string reason, string backlogLabel)
        : this(entry, () => coverImagePath, reason, backlogLabel) { }
    internal LibraryCardViewModel(GameEntry entry, Func<string?> resolveCover, string reason, string backlogLabel)
    { Entry = entry; _resolveCover = resolveCover; Reason = reason; BacklogLabel = backlogLabel; }
    public GameEntry Entry { get; }
    public string? CoverImagePath => _resolveCover();
    public string Reason { get; }
    public string BacklogLabel { get; }
    public string Title => Entry.Title;
    public uint? AppId => Entry.SteamAppId;
    public override string ToString() => Title;
}
public sealed record HistoryRow(uint AppId, string Title, string Time)
{
    public override string ToString() => Title;
}

public sealed partial class MainViewModel
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<GameIdentifier> _drawnCycle = new();
    private SynchronizationContext? _uiContext;
    private ILibrarySynchronization? _synchronization;
    private BacklogStore _backlog = null!;
    private string _accountId = "local";
    private bool _initialized, _isRefreshing, _isConnecting;
    private volatile bool _vmDisposed;
    private int _libraryVersion, _installedCount, _familyCount, _missingNameCount;
    private readonly Dictionary<GameIdentifier, LibraryCardViewModel> _cardCache = new();
    private IReadOnlyList<LibraryCardViewModel> _suggestionCandidates = Array.Empty<LibraryCardViewModel>();
    private string? _lastRefreshError;
    private string _searchText = "", _activePage = "discover", _ownershipFilter = "all", _backlogFilter = "active";
    private bool _avoidRepeats = true, _advancedFilters;
    private IReadOnlyList<LibraryCardViewModel> _visibleGames = Array.Empty<LibraryCardViewModel>();
    private IReadOnlyList<LibraryCardViewModel> _suggestions = Array.Empty<LibraryCardViewModel>();
    private (string Account, uint Id, BacklogStatus Previous)? _undo;
    private CancellationTokenSource? _loginCancellation;

    public RelayCommand NavigateCommand { get; private set; } = null!;
    public RelayCommand SelectGameCommand { get; private set; } = null!;
    public RelayCommand SetOwnershipFilterCommand { get; private set; } = null!;
    public RelayCommand SetBacklogFilterCommand { get; private set; } = null!;
    public RelayCommand SetBacklogStatusCommand { get; private set; } = null!;
    public RelayCommand UndoBacklogCommand { get; private set; } = null!;
    public RelayCommand ResetFiltersCommand { get; private set; } = null!;
    public RelayCommand ClearHistoryCommand { get; private set; } = null!;
    public RelayCommand CancelSyncCommand { get; private set; } = null!;
    public AsyncRelayCommand ConnectSteamCommand { get; private set; } = null!;
    public AsyncRelayCommand DisconnectSteamCommand { get; private set; } = null!;

    public ObservableCollection<HistoryRow> History { get; } = new SnapshotCollection<HistoryRow>();
    public IReadOnlyList<LibraryCardViewModel> VisibleGames => _visibleGames;
    public IReadOnlyList<LibraryCardViewModel> Suggestions => _suggestions;
    public bool HasSuggestions => _suggestions.Count > 0;
    public int TotalGameCount => _library.Count;
    public int EligibleGameCount => _eligibleGameCount;
    public int InstalledGameCount => _installedCount;
    public int FamilyGameCount => _familyCount;
    public int MissingNameCount => _missingNameCount;
    public bool HasNoMatches => !IsRefreshing && _eligibleGameCount == 0;
    public bool HasLibrary => _library.Count > 0;
    public bool HasHistory => History.Count > 0;
    public bool CanUndoBacklog => _undo.HasValue && _undo.Value.Account == _accountId;
    public bool IsDiscoverPage => ActivePage == "discover";
    public bool IsLibraryPage => ActivePage == "library";
    public bool IsHistoryPage => ActivePage == "history";
    public bool IsSettingsPage => ActivePage == "settings";
    public string ActivePage
    {
        get => _activePage;
        private set
        {
            if (!SetProperty(ref _activePage, value)) return;
            AdvancedFilters = false;
            foreach (var property in new[] { nameof(IsDiscoverPage), nameof(IsLibraryPage), nameof(IsHistoryPage), nameof(IsSettingsPage), nameof(PageTitle), nameof(PageSubtitle) })
                OnPropertyChanged(property);
        }
    }
    public string PageTitle => L("Workspace_" + ActivePage);
    public string PageSubtitle => L("Workspace_" + ActivePage + "_Subtitle");
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value ?? "")) UpdateEligibilitySummary(); } }
    public string OwnershipFilter { get => _ownershipFilter; set { if (SetProperty(ref _ownershipFilter, value)) UpdateEligibilitySummary(); } }
    public string BacklogFilter { get => _backlogFilter; set { if (SetProperty(ref _backlogFilter, value)) UpdateEligibilitySummary(); } }
    public bool AvoidRepeats
    {
        get => _avoidRepeats;
        set
        {
            if (_avoidRepeats == value) return;
            try { _backlog.SetAvoidRepeats(_accountId, value); SetProperty(ref _avoidRepeats, value); }
            catch (Exception ex) { SetStatusRaw(ex.Message); OnPropertyChanged(); }
        }
    }
    public bool AdvancedFilters { get => _advancedFilters; set => SetProperty(ref _advancedFilters, value); }
    public int AdvancedFilterCount
    {
        get
        {
            var filters = _selectionEngine.GetPreferences().Filters;
            return (string.IsNullOrWhiteSpace(filters.RequiredCollection) ? 0 : 1)
                + (filters.ExcludeDeckUnsupported ? 1 : 0)
                + (filters.RequireMacCompatible ? 1 : 0)
                + (filters.IncludedCategories.Count == 1 && filters.IncludedCategories[0] == ProductCategory.Game ? 0 : 1);
        }
    }
    public bool HasAdvancedFilterCount => AdvancedFilterCount > 0;
    public bool IsRefreshing { get => _isRefreshing; private set { SetProperty(ref _isRefreshing, value); OnPropertyChanged(nameof(HasNoMatches)); } }
    public string? LastRefreshError { get => _lastRefreshError; private set => SetProperty(ref _lastRefreshError, value); }
    public bool IsConnecting { get => _isConnecting; private set { SetProperty(ref _isConnecting, value); OnPropertyChanged(nameof(IsSyncBusy)); } }
    public bool HasSynchronization => _synchronization is not null;
    public bool IsConnected => _synchronization?.Status.IsConnected == true;
    public bool IsSyncBusy => IsConnecting || _synchronization?.Status.IsBusy == true;
    public string? QrChallengeUrl => _synchronization?.Status.QrChallengeUrl;
    public bool HasQrChallenge => !string.IsNullOrWhiteSpace(QrChallengeUrl);
    public string AccountSummary => _accountId == "local" ? L("Workspace_LocalAccount") : L("Workspace_Account") + " " + _accountId;
    public string SyncSummary => _synchronization is null ? L("Workspace_LocalMode") : _synchronization.Status.Message;
    public string? SyncError => _synchronization?.Status.Error;
    public string LastSyncSummary => _synchronization?.Status.LastUpdated is { } time
        ? L("Workspace_Updated") + " " + time.ToLocalTime().ToString("HH:mm")
        : L("Workspace_LocalCoverage");
    public bool NetworkEnabled
    {
        get => _synchronization?.NetworkEnabled ?? false;
        set { if (_synchronization is null) return; _synchronization.NetworkEnabled = value; OnPropertyChanged(); }
    }
    public string SelectionReason => _selectedGameEntry is null ? "" : ReasonFor(_selectedGameEntry);
    public string SelectedBacklogLabel => _selectedGameEntry?.SteamAppId is uint id ? StatusLabel(_backlog.GetStatus(_accountId, id)) : "";
    public string EmptyTitle => L(HasLibrary ? "Workspace_NoMatches" : "Workspace_NoLibrary");
    public string EmptyDescription => L(HasLibrary ? "Workspace_NoMatchesHelp" : "Workspace_NoLibraryHelp");
    public string LibraryCountSummary => _localizationService.GetString("Workspace_CountSummary", _eligibleGameCount, _library.Count);
    private string L(string key) => _localizationService.GetString(key);

    private void InitializeWorkspace(BacklogStore backlog)
    {
        _backlog = backlog;
        _avoidRepeats = backlog.GetAvoidRepeats(_accountId);
        _synchronization = _libraryService as ILibrarySynchronization;
        if (_synchronization is not null)
        {
            _synchronization.Language = CurrentLanguage;
            _synchronization.SnapshotChanged += OnSnapshotChanged;
            _synchronization.StatusChanged += OnSyncStatusChanged;
        }
        NavigateCommand = new RelayCommand(value => { if (value is string page && page is "discover" or "library" or "history" or "settings") ActivePage = page; });
        SelectGameCommand = new RelayCommand(value =>
        {
            var game = value switch { LibraryCardViewModel card => _library.FirstOrDefault(g => g.Id == card.Entry.Id), HistoryRow row => _library.FirstOrDefault(g => g.SteamAppId == row.AppId), _ => null };
            if (game is not null) { PreviewSelection(game); ActivePage = "discover"; }
        });
        SetOwnershipFilterCommand = new RelayCommand(value => OwnershipFilter = value as string ?? "all");
        SetBacklogFilterCommand = new RelayCommand(value => BacklogFilter = value as string ?? "active");
        ResetFiltersCommand = new RelayCommand(() =>
        {
            _searchText = ""; _ownershipFilter = "all"; _backlogFilter = "active";
            Preferences.ResetFilters();
            OnPropertyChanged(nameof(SearchText)); OnPropertyChanged(nameof(OwnershipFilter)); OnPropertyChanged(nameof(BacklogFilter));
            UpdateEligibilitySummary();
        });
        SetBacklogStatusCommand = new RelayCommand(SetBacklogStatus, _ => HasSelection);
        UndoBacklogCommand = new RelayCommand(UndoBacklog, () => CanUndoBacklog);
        ClearHistoryCommand = new RelayCommand(() =>
        {
            try { _backlog.ClearHistory(_accountId); _drawnCycle.Clear(); RefreshHistory(); }
            catch (Exception ex) { SetStatusRaw(ex.Message); }
        });
        ConnectSteamCommand = new AsyncRelayCommand(ConnectSteamAsync, () => HasSynchronization && !IsConnected);
        DisconnectSteamCommand = new AsyncRelayCommand(DisconnectSteamAsync, () => IsConnected);
        CancelSyncCommand = new RelayCommand(() => { _loginCancellation?.Cancel(); _synchronization?.CancelSynchronization(); });
    }

    private void AcceptSnapshot(IReadOnlyList<GameEntry> games, string accountId)
    {
        if (_vmDisposed) return;
        var accountChanged = _accountId != accountId;
        var ordered = games.OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (!accountChanged && _library.SequenceEqual(ordered))
        {
            // Refresh has already shown "loading". Restore the effective summary even when
            // the ItemsSource can be retained, including an initially empty library.
            UpdateEligibilitySummary();
            return;
        }
        _libraryVersion++;
        if (accountChanged)
        {
            _cardCache.Clear();
            _accountId = accountId; _drawnCycle.Clear(); _undo = null; ResetSelection();
            _avoidRepeats = _backlog.GetAvoidRepeats(accountId);
            OnPropertyChanged(nameof(AvoidRepeats));
            OnPropertyChanged(nameof(AccountSummary)); OnPropertyChanged(nameof(CanUndoBacklog));
        }
        var previousId = _selectedGameEntry?.Id;
        _library.Clear();
        _library.AddRange(ordered);
        _installedCount = _library.Count(game => game.InstallState == InstallState.Installed);
        _familyCount = _library.Count(game => game.OwnershipType == OwnershipType.FamilyShared);
        _missingNameCount = _library.Count(game => game.Title.StartsWith("App ", StringComparison.Ordinal));
        var identifiers = _library.Select(game => game.Id).ToHashSet();
        foreach (var id in _cardCache.Keys.Where(id => !identifiers.Contains(id)).ToArray()) _cardCache.Remove(id);
        _drawnCycle.IntersectWith(identifiers);
        Preferences.UpdateCollections(GetAvailableCollections());
        if (previousId is not null && _library.FirstOrDefault(g => g.Id == previousId) is { } selected) PreviewSelection(selected);
        else if (previousId is not null) ResetSelection();
        UpdateEligibilitySummary();
        if (!HasSelection && _visibleGames.FirstOrDefault() is { } first) PreviewSelection(first.Entry);
        RefreshHistory();
    }

    private IEnumerable<GameEntry> FilterWorkspaceGames()
    {
        return _library.Where(game =>
        {
            if (!string.IsNullOrWhiteSpace(SearchText) && !game.Title.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
                && !(game.SteamAppId?.ToString().Contains(SearchText, StringComparison.Ordinal) ?? false)) return false;
            if (OwnershipFilter == "family" && game.OwnershipType != OwnershipType.FamilyShared) return false;
            if (OwnershipFilter == "owned" && game.OwnershipType != OwnershipType.Owned) return false;
            var status = game.SteamAppId is uint id ? _backlog.GetStatus(_accountId, id) : BacklogStatus.None;
            return BacklogFilter switch
            {
                "all" => true, "want" => status == BacklogStatus.WantToPlay, "playing" => status == BacklogStatus.Playing,
                "completed" => status == BacklogStatus.Completed, "hidden" => status == BacklogStatus.Hidden,
                "later" => status == BacklogStatus.Later,
                _ => status is BacklogStatus.None or BacklogStatus.WantToPlay or BacklogStatus.Playing
            };
        });
    }

    private void PopulateCards(IReadOnlyList<GameEntry> games)
    {
        var cards = new LibraryCardViewModel[games.Count];
        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            if (!_cardCache.TryGetValue(game.Id, out var card) || card.Entry != game)
            {
                var reason = ReasonFor(game);
                var label = game.SteamAppId is uint id ? StatusLabel(_backlog.GetStatus(_accountId, id)) : "";
                // Only realized controls ask for the path; filtering never probes every game's filesystem.
                card = new LibraryCardViewModel(game, () => _artLocator.FindHeroImage(game), reason, label);
                _cardCache[game.Id] = card;
            }
            cards[index] = card;
        }
        if (_visibleGames.SequenceEqual(cards)) return;
        _visibleGames = cards;
        _suggestionCandidates = cards.OrderByDescending(card => card.Entry.InstallState == InstallState.Installed)
            .ThenByDescending(card => card.Entry.SteamAppId is uint id && _backlog.GetStatus(_accountId, id) == BacklogStatus.WantToPlay)
            .Take(4).ToArray();
        OnPropertyChanged(nameof(VisibleGames));
        UpdateSuggestions();
    }

    private void UpdateSuggestions()
    {
        var suggestions = _suggestionCandidates.Where(card => card.Entry.Id != _selectedGameEntry?.Id).Take(3).ToArray();
        if (_suggestions.SequenceEqual(suggestions)) return;
        _suggestions = suggestions;
        OnPropertyChanged(nameof(Suggestions)); OnPropertyChanged(nameof(HasSuggestions));
    }

    private string ReasonFor(GameEntry game) => game.InstallState == InstallState.Installed ? L("Workspace_ReasonInstalled")
        : game.OwnershipType == OwnershipType.FamilyShared ? L("Workspace_ReasonFamily")
        : game.OwnershipType == OwnershipType.Owned ? L("Workspace_ReasonOwned") : L("Workspace_ReasonLocal");
    private string StatusLabel(BacklogStatus status) => L("Backlog_" + status);
    private void PreviewSelection(GameEntry game)
    {
        _selectedGameEntry = game;
        SelectedGame = GameDetailsViewModel.FromGame(game, _artLocator.FindHeroImage(game), _localizationService, _gameLaunchService.GetLaunchOptions(game));
    }
    private void SetBacklogStatus(object? value)
    {
        if (_selectedGameEntry?.SteamAppId is not uint id || value is not string text || !Enum.TryParse<BacklogStatus>(text, out var status) || !Enum.IsDefined(status)) return;
        try
        {
            var previous = _backlog.GetStatus(_accountId, id);
            _backlog.SetStatus(_accountId, id, status);
            _cardCache.Remove(GameIdentifier.ForSteam(id));
            _undo = (_accountId, id, previous);
            UpdateEligibilitySummary(); NotifySelectionChanged();
            SetStatus(loc => loc.GetString("Workspace_BacklogSaved"));
        }
        catch (Exception ex) { SetStatusRaw(ex.Message); }
    }
    private void UndoBacklog()
    {
        if (_undo is not { } undo || undo.Account != _accountId) return;
        try
        {
            _backlog.SetStatus(_accountId, undo.Id, undo.Previous);
            _cardCache.Remove(GameIdentifier.ForSteam(undo.Id));
            _undo = null; UpdateEligibilitySummary(); NotifySelectionChanged();
        }
        catch (Exception ex) { SetStatusRaw(ex.Message); }
    }
    private void RefreshHistory()
    {
        var next = _backlog.GetHistory(_accountId).Select(item =>
            new HistoryRow(item.AppId, item.Title, item.SelectedAt.ToLocalTime().ToString("g"))).ToArray();
        if (History.SequenceEqual(next)) return;
        if (next.Length > 0 && next.Skip(1).SequenceEqual(History.Take(next.Length - 1)) &&
            (next.Length == History.Count + 1 || next.Length == History.Count))
        {
            if (History.Count == next.Length) History.RemoveAt(History.Count - 1);
            History.Insert(0, next[0]);
        }
        else ((SnapshotCollection<HistoryRow>)History).ReplaceWith(next);
        OnPropertyChanged(nameof(HasHistory));
    }
    private void NotifySelectionChanged()
    {
        UpdateSuggestions();
        OnPropertyChanged(nameof(SelectionReason)); OnPropertyChanged(nameof(SelectedBacklogLabel));
        OnPropertyChanged(nameof(CanUndoBacklog));
        SetBacklogStatusCommand?.RaiseCanExecuteChanged(); UndoBacklogCommand?.RaiseCanExecuteChanged();
    }
    private void NotifyLibraryChanged()
    {
        foreach (var property in new[] { nameof(TotalGameCount), nameof(EligibleGameCount), nameof(InstalledGameCount),
            nameof(FamilyGameCount), nameof(MissingNameCount), nameof(HasNoMatches), nameof(HasLibrary),
            nameof(EmptyTitle), nameof(EmptyDescription), nameof(LibraryCountSummary),
            nameof(AdvancedFilterCount), nameof(HasAdvancedFilterCount) }) OnPropertyChanged(property);
    }
    private void NotifyWorkspaceLocalization()
    {
        foreach (var property in new[] { nameof(PageTitle), nameof(PageSubtitle), nameof(AccountSummary), nameof(SyncSummary), nameof(LastSyncSummary) }) OnPropertyChanged(property);
        NotifySelectionChanged(); NotifyLibraryChanged();
    }
    private void Dispatch(Action action)
    {
        if (_vmDisposed) return;
        if (_uiContext is null || SynchronizationContext.Current == _uiContext) action();
        else _uiContext.Post(_ => { if (!_vmDisposed) action(); }, null);
    }
    private void OnSnapshotChanged(object? sender, LibrarySnapshotEventArgs e) => Dispatch(() =>
    {
        var status = _synchronization?.Status;
        if (status?.Generation == e.Generation && status.AccountId == e.AccountId) AcceptSnapshot(e.Games, e.AccountId);
    });
    private void OnSyncStatusChanged(object? sender, EventArgs e) => Dispatch(() =>
    {
        foreach (var property in new[] { nameof(IsConnected), nameof(IsSyncBusy), nameof(QrChallengeUrl), nameof(HasQrChallenge),
            nameof(SyncSummary), nameof(SyncError), nameof(LastSyncSummary), nameof(NetworkEnabled) }) OnPropertyChanged(property);
        ConnectSteamCommand.RaiseCanExecuteChanged(); DisconnectSteamCommand.RaiseCanExecuteChanged();
    });
    private async Task ConnectSteamAsync()
    {
        if (_vmDisposed || _synchronization is null) return;
        _loginCancellation?.Dispose();
        _loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        IsConnecting = true;
        try { await _synchronization.ConnectAsync(_loginCancellation.Token); await RefreshLibraryAsync(); }
        catch (OperationCanceledException) { SetStatus(loc => loc.GetString("Workspace_Canceled")); }
        catch (Exception ex) { SetStatusRaw(ex.Message); }
        finally { IsConnecting = false; OnSyncStatusChanged(this, EventArgs.Empty); }
    }
    private async Task DisconnectSteamAsync()
    {
        if (_vmDisposed || _synchronization is null) return;
        try { await _synchronization.DisconnectAsync(_lifetime.Token); await RefreshLibraryAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatusRaw(ex.Message); }
        finally { OnSyncStatusChanged(this, EventArgs.Empty); }
    }
    public void Dispose()
    {
        if (_vmDisposed) return;
        _vmDisposed = true;
        _lifetime.Cancel(); _loginCancellation?.Cancel();
        _synchronization?.CancelSynchronization();
        _localizationService.LanguageChanged -= OnLanguageChanged;
        Preferences.PreferencesChanged -= OnPreferencesChanged;
        Preferences.Dispose();
        _cardCache.Clear();
        if (_synchronization is not null)
        {
            _synchronization.SnapshotChanged -= OnSnapshotChanged;
            _synchronization.StatusChanged -= OnSyncStatusChanged;
        }
        _loginCancellation?.Dispose(); _lifetime.Dispose();
    }
}
