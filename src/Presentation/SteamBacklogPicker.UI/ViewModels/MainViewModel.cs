using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Domain;
using Domain.Selection;
using SteamBacklogPicker.UI.Services;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ISelectionEngine _selectionEngine;
    private readonly IGameLibraryService _libraryService;
    private readonly IGameArtLocator _artLocator;
    private readonly IToastNotificationService _toastNotificationService;
    private readonly List<GameEntry> _library = new();
    private int _eligibleGameCount;
    private GameDetailsViewModel _selectedGame = GameDetailsViewModel.Empty;
    private bool _isDrawing;

    public MainViewModel(
        ISelectionEngine selectionEngine,
        IGameLibraryService libraryService,
        IGameArtLocator artLocator,
        IToastNotificationService toastNotificationService)
    {
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _artLocator = artLocator ?? throw new ArgumentNullException(nameof(artLocator));
        _toastNotificationService = toastNotificationService ?? throw new ArgumentNullException(nameof(toastNotificationService));

        Preferences = new SelectionPreferencesViewModel(_selectionEngine);
        Preferences.PreferencesChanged += OnPreferencesChanged;

        RefreshCommand = new AsyncRelayCommand(RefreshLibraryAsync);
        DrawCommand = new AsyncRelayCommand(DrawAsync, () => _eligibleGameCount > 0);
        LaunchCommand = new RelayCommand(LaunchGame, () => SelectedGame.CanLaunch);
        InstallCommand = new RelayCommand(InstallGame, () => SelectedGame.CanInstall);
    }

    public SelectionPreferencesViewModel Preferences { get; }

    public AsyncRelayCommand DrawCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand LaunchCommand { get; }

    public RelayCommand InstallCommand { get; }

    public GameDetailsViewModel SelectedGame
    {
        get => _selectedGame;
        private set
        {
            if (_selectedGame == value)
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

    private string _statusMessage = "Carregando biblioteca...";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasSelection => SelectedGame != GameDetailsViewModel.Empty;

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
        StatusMessage = "Carregando biblioteca...";
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
            StatusMessage = ex.Message;
            Preferences.UpdateCollections(Array.Empty<string>());
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
            SelectedGame = GameDetailsViewModel.Empty;
            StatusMessage = "Sorteando...";
            await Task.Delay(850).ConfigureAwait(true);

            var game = _selectionEngine.PickNext(_library);
            ApplySelection(game);
            StatusMessage = $"Jogo sorteado: {game.Title}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsDrawing = false;
        }
    }

    private void ApplySelection(GameEntry game)
    {
        var coverPath = _artLocator.FindHeroImage(game.AppId);
        var details = GameDetailsViewModel.FromGame(game, coverPath);
        SelectedGame = details;
        _toastNotificationService.ShowGameSelected(game, coverPath);
    }

    private void LaunchGame()
    {
        if (!SelectedGame.CanLaunch)
        {
            return;
        }

        OpenSteamUri($"steam://run/{SelectedGame.AppId}");
    }

    private void InstallGame()
    {
        if (!SelectedGame.CanInstall)
        {
            return;
        }

        OpenSteamUri($"steam://install/{SelectedGame.AppId}");
    }

    private void OpenSteamUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
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
            StatusMessage = ex.Message;
            DrawCommand.RaiseCanExecuteChanged();
            return;
        }

        _eligibleGameCount = eligibleGames.Count;
        var total = _library.Count;

        if (SelectedGame != GameDetailsViewModel.Empty &&
            !eligibleGames.Any(game => game.AppId == SelectedGame.AppId))
        {
            SelectedGame = GameDetailsViewModel.Empty;
        }

        if (total == 0)
        {
            StatusMessage = "Nenhum jogo encontrado nos diretórios configurados.";
        }
        else if (_eligibleGameCount == 0)
        {
            StatusMessage = $"Nenhum jogo corresponde aos filtros atuais (0 de {FormatGameCount(total)}).";
        }
        else if (_eligibleGameCount == total)
        {
            StatusMessage = $"{FormatGameCount(total)} disponíveis para sorteio.";
        }
        else
        {
            StatusMessage = $"{FormatGameCount(_eligibleGameCount)} disponíveis após aplicar os filtros (de {FormatGameCount(total)}).";
        }

        DrawCommand.RaiseCanExecuteChanged();
    }

    private static string FormatGameCount(int count)
        => count == 1 ? "1 jogo" : $"{count} jogos";
}
