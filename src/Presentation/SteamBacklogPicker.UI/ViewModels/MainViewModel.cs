using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Domain;
using Domain.Selection;
using SteamBacklogPicker.UI.Services.GameArt;
using SteamBacklogPicker.UI.Services.Launch;
using SteamBacklogPicker.UI.Services.Library;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.Services.Notifications;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ISelectionEngine _selectionEngine;
    private readonly IGameLibraryService _libraryService;
    private readonly IGameArtLocator _artLocator;
    private readonly IToastNotificationService _toastNotificationService;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly ILocalizationService _localizationService;
    private readonly List<GameEntry> _library = new();
    private readonly GameDetailsViewModel _emptyGame;
    private int _eligibleGameCount;
    private GameDetailsViewModel _selectedGame;
    private bool _isDrawing;
    private Func<ILocalizationService, string>? _statusFactory;
    private string _statusMessage = string.Empty;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;
    private GameEntry? _selectedGameEntry;

    public MainViewModel(
        ISelectionEngine selectionEngine,
        IGameLibraryService libraryService,
        IGameArtLocator artLocator,
        IToastNotificationService toastNotificationService,
        ILocalizationService localizationService,
        IGameLaunchService gameLaunchService,
        Func<ProcessStartInfo, Process?>? processStarter = null)
    {
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _artLocator = artLocator ?? throw new ArgumentNullException(nameof(artLocator));
        _toastNotificationService = toastNotificationService ?? throw new ArgumentNullException(nameof(toastNotificationService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _gameLaunchService = gameLaunchService ?? throw new ArgumentNullException(nameof(gameLaunchService));
        _processStarter = processStarter ?? Process.Start;

        _localizationService.LanguageChanged += OnLanguageChanged;

        Preferences = new SelectionPreferencesViewModel(_selectionEngine, _localizationService);
        Preferences.PreferencesChanged += OnPreferencesChanged;

        _emptyGame = GameDetailsViewModel.CreateEmpty(_localizationService);
        _selectedGame = _emptyGame;

        RefreshCommand = new AsyncRelayCommand(RefreshLibraryAsync);
        DrawCommand = new AsyncRelayCommand(DrawAsync, () => _eligibleGameCount > 0);
        LaunchCommand = new RelayCommand(LaunchGame, () => SelectedGame.CanLaunch);
        InstallCommand = new RelayCommand(InstallGame, () => SelectedGame.CanInstall);
        ChangeLanguageCommand = new RelayCommand(ChangeLanguage);

        SetStatus(loc => loc.GetString("Status_LoadingLibrary"));
    }

    public SelectionPreferencesViewModel Preferences { get; }

    public AsyncRelayCommand DrawCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand LaunchCommand { get; }

    public RelayCommand InstallCommand { get; }

    public RelayCommand ChangeLanguageCommand { get; }

    public string CurrentLanguage => _localizationService.CurrentLanguage;

    public GameDetailsViewModel SelectedGame
    {
        get => _selectedGame;
        private set
        {
            if (ReferenceEquals(_selectedGame, value))
            {
                return;
            }

            _selectedGame = value;

            OnPropertyChanged();
            LaunchCommand.RaiseCanExecuteChanged();
            InstallCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasSelection => !ReferenceEquals(SelectedGame, _emptyGame);

    public bool IsDrawing
    {
        get => _isDrawing;
        private set => SetProperty(ref _isDrawing, value);
    }

    public async Task InitializeAsync()
    {
        await RefreshLibraryAsync().ConfigureAwait(true);
    }

    private async Task RefreshLibraryAsync()
    {
        SetStatus(loc => loc.GetString("Status_LoadingLibrary"));
        ResetSelection();
        _library.Clear();
        try
        {
            var games = await _libraryService.GetLibraryAsync().ConfigureAwait(true);
            _library.AddRange(games.OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase));
            Preferences.UpdateCollections(GetAvailableCollections());
            UpdateEligibilitySummary();
        }
        catch (Exception ex)
        {
            _eligibleGameCount = 0;
            SetStatusRaw(ex.Message);
            Preferences.UpdateCollections(Array.Empty<string>());
            ResetSelection();
        }
        finally
        {
            DrawCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task DrawAsync()
    {
        if (_library.Count == 0)
        {
            await RefreshLibraryAsync().ConfigureAwait(true);
            if (_library.Count == 0)
            {
                return;
            }
        }

        try
        {
            IsDrawing = true;
            ResetSelection();
            SetStatus(loc => loc.GetString("Status_Drawing"));
            await Task.Delay(850).ConfigureAwait(true);

            var game = _selectionEngine.PickNext(_library);
            ApplySelection(game);
            SetStatus(loc => loc.GetString("Status_Drawn", game.Title));
        }
        catch (Exception ex)
        {
            SetStatusRaw(ex.Message);
        }
        finally
        {
            IsDrawing = false;
        }
    }

    private void ApplySelection(GameEntry game)
    {
        var launchOptions = _gameLaunchService.GetLaunchOptions(game);
        var coverPath = _artLocator.FindHeroImage(game);
        var details = GameDetailsViewModel.FromGame(game, coverPath, _localizationService, launchOptions);
        _selectedGameEntry = game;
        SelectedGame = details;
        _toastNotificationService.ShowGameSelected(game, coverPath);
    }

    private void LaunchGame()
    {
        if (_selectedGameEntry is null)
        {
            HandleActionFailure(_localizationService.GetString("Status_NoGamesFound"));
            return;
        }

        var launchOptions = _gameLaunchService.GetLaunchOptions(_selectedGameEntry);
        SelectedGame.UpdateLaunchOptions(launchOptions);
        LaunchCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();

        var launchAction = launchOptions.Launch;
        if (!launchAction.IsSupported)
        {
            HandleActionFailure(launchAction.ErrorMessage);
            return;
        }

        if (launchAction.ProcessStartInfo is null)
        {
            HandleActionFailure("Launch information for this game is not available.");
            return;
        }

        TryStartProcess(launchAction.ProcessStartInfo);
    }

    private void InstallGame()
    {
        if (_selectedGameEntry is null)
        {
            HandleActionFailure(_localizationService.GetString("Status_NoGamesFound"));
            return;
        }

        var launchOptions = _gameLaunchService.GetLaunchOptions(_selectedGameEntry);
        SelectedGame.UpdateLaunchOptions(launchOptions);
        LaunchCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();

        var installAction = launchOptions.Install;
        if (!installAction.IsSupported)
        {
            HandleActionFailure(installAction.ErrorMessage);
            return;
        }

        if (installAction.ProcessStartInfo is null)
        {
            HandleActionFailure("Installation information for this game is not available.");
            return;
        }

        TryStartProcess(installAction.ProcessStartInfo);
    }

    private void TryStartProcess(ProcessStartInfo startInfo)
    {
        try
        {
            _processStarter(startInfo);
        }
        catch (Exception ex)
        {
            SetStatusRaw(ex.Message);
        }
    }

    private void HandleActionFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "This action is not available for the selected game.";
        }

        SetStatusRaw(message);
    }

    private void ResetSelection()
    {
        _selectedGameEntry = null;
        _emptyGame.UpdateLaunchOptions(GameLaunchOptions.Empty);
        if (!ReferenceEquals(SelectedGame, _emptyGame))
        {
            SelectedGame = _emptyGame;
        }
        else
        {
            LaunchCommand.RaiseCanExecuteChanged();
            InstallCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnPreferencesChanged(object? sender, SelectionPreferences preferences)
    {
        UpdateEligibilitySummary();
    }

    private IEnumerable<string> GetAvailableCollections()
    {
        return _library
            .SelectMany(game => game.Tags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateEligibilitySummary()
    {
        IReadOnlyList<GameEntry> eligibleGames;
        try
        {
            eligibleGames = _selectionEngine.FilterGames(_library);
        }
        catch (Exception ex)
        {
            _eligibleGameCount = 0;
            SetStatusRaw(ex.Message);
            DrawCommand.RaiseCanExecuteChanged();
            return;
        }

        _eligibleGameCount = eligibleGames.Count;
        var total = _library.Count;

        if (!ReferenceEquals(SelectedGame, _emptyGame) &&
            !eligibleGames.Any(game => game.Id == SelectedGame.Id))
        {
            ResetSelection();
        }

        if (total == 0)
        {
            SetStatus(loc => loc.GetString("Status_NoGamesFound"));
        }
        else if (_eligibleGameCount == 0)
        {
            SetStatus(loc => loc.GetString("Status_NoMatches", loc.FormatGameCount(total)));
        }
        else if (_eligibleGameCount == total)
        {
            SetStatus(loc => loc.GetString("Status_AllEligible", loc.FormatGameCount(total)));
        }
        else
        {
            SetStatus(loc => loc.GetString(
                "Status_FilteredCount",
                loc.FormatGameCount(_eligibleGameCount),
                loc.FormatGameCount(total)));
        }

        DrawCommand.RaiseCanExecuteChanged();
    }

    private void ChangeLanguage(object? parameter)
    {
        if (parameter is not string languageCode)
        {
            return;
        }

        _localizationService.SetLanguage(languageCode);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _emptyGame.RefreshLocalization();
        SelectedGame.RefreshLocalization();
        Preferences.RefreshLocalization();
        ReapplyStatus();
        OnPropertyChanged(nameof(CurrentLanguage));
    }

    private void SetStatus(Func<ILocalizationService, string> statusFactory)
    {
        _statusFactory = statusFactory ?? throw new ArgumentNullException(nameof(statusFactory));
        StatusMessage = statusFactory(_localizationService);
    }

    private void SetStatusRaw(string message)
    {
        _statusFactory = null;
        StatusMessage = message;
    }

    private void ReapplyStatus()
    {
        if (_statusFactory is null)
        {
            return;
        }

        StatusMessage = _statusFactory(_localizationService);
    }
}
