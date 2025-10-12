using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private SelectionPreferences _currentPreferences;
    private GameDetailsViewModel _selectedGame = GameDetailsViewModel.Empty;

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

        _currentPreferences = _selectionEngine.GetPreferences();
        Preferences = new SelectionPreferencesViewModel(_selectionEngine);
        Preferences.PreferencesChanged += OnPreferencesChanged;

        DrawCommand = new AsyncRelayCommand(DrawAsync, () => _library.Count > 0);
        LaunchCommand = new RelayCommand(LaunchGame, () => SelectedGame.CanLaunch);
        InstallCommand = new RelayCommand(InstallGame, () => SelectedGame.CanInstall);
    }

    public SelectionPreferencesViewModel Preferences { get; }

    public AsyncRelayCommand DrawCommand { get; }

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

            if (_selectedGame is not null)
            {
                _selectedGame.PropertyChanged -= OnSelectedGamePropertyChanged;
            }

            _selectedGame = value;
            if (_selectedGame is not null)
            {
                _selectedGame.PropertyChanged += OnSelectedGamePropertyChanged;
            }

            OnPropertyChanged();
            LaunchCommand.RaiseCanExecuteChanged();
            InstallCommand.RaiseCanExecuteChanged();
        }
    }

    private string _statusMessage = "Carregando biblioteca...";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public async Task InitializeAsync()
    {
        await RefreshLibraryAsync().ConfigureAwait(true);
        UpdateSelectedGameFavoriteState();
    }

    private async Task RefreshLibraryAsync()
    {
        StatusMessage = "Carregando biblioteca...";
        try
        {
            _library.Clear();
            var games = await _libraryService.GetLibraryAsync().ConfigureAwait(true);
            _library.AddRange(games.OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase));
            StatusMessage = _library.Count == 0
                ? "Nenhum jogo encontrado nos diretórios configurados."
                : $"{_library.Count} jogos disponíveis para sorteio.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
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
            var game = _selectionEngine.PickNext(_library);
            ApplySelection(game);
            StatusMessage = $"Jogo sorteado: {game.Title}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void ApplySelection(GameEntry game)
    {
        var coverPath = _artLocator.FindHeroImage(game.AppId);
        var isFavorite = _currentPreferences.GameWeights.TryGetValue(game.AppId, out var weight) && weight > 1.0;
        var details = GameDetailsViewModel.FromGame(game, coverPath, isFavorite);
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
        _currentPreferences = preferences;
        UpdateSelectedGameFavoriteState();
    }

    private void UpdateSelectedGameFavoriteState()
    {
        if (SelectedGame == GameDetailsViewModel.Empty)
        {
            return;
        }

        var isFavorite = _currentPreferences.GameWeights.TryGetValue(SelectedGame.AppId, out var weight) && weight > 1.0;
        SelectedGame.IsFavorite = isFavorite;
    }

    private void OnSelectedGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(GameDetailsViewModel.IsFavorite), StringComparison.Ordinal))
        {
            return;
        }

        if (SelectedGame == GameDetailsViewModel.Empty)
        {
            return;
        }

        var preferences = _selectionEngine.GetPreferences();
        if (SelectedGame.IsFavorite)
        {
            preferences.GameWeights[SelectedGame.AppId] = Math.Max(
                preferences.GameWeights.TryGetValue(SelectedGame.AppId, out var currentWeight) ? currentWeight : 0.0,
                2.0);
        }
        else
        {
            preferences.GameWeights.Remove(SelectedGame.AppId);
        }

        _selectionEngine.UpdatePreferences(preferences);
        _currentPreferences = preferences;
        Preferences.Apply(preferences);
    }
}
