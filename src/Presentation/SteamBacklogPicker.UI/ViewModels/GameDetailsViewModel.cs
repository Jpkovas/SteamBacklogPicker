using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Domain;
using SteamBacklogPicker.UI.Services;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class GameDetailsViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private readonly IGameUserDataService _userDataService;
    private readonly bool _isPlaceholder;
    private string _title;
    private IReadOnlyList<string> _tags;
    private List<BacklogStatusOption> _statusOptions = new();
    private BacklogStatusOption _selectedStatusOption = new(BacklogStatus.Unspecified, string.Empty);
    private string _personalNotes = string.Empty;
    private string _playtimeHoursText = string.Empty;
    private string _targetSessionHoursText = string.Empty;
    private string _estimatedCompletionText = string.Empty;
    private string _progressSummary = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isSaving;
    private GameUserData _userData = GameUserData.Empty;

    private GameDetailsViewModel(
        ILocalizationService localizationService,
        IGameUserDataService userDataService,
        uint appId,
        string title,
        string? coverImagePath,
        InstallState installState,
        OwnershipType ownershipType,
        IReadOnlyList<string> tags,
        GameUserData userData,
        bool isPlaceholder)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _userDataService = userDataService ?? throw new ArgumentNullException(nameof(userDataService));
        AppId = appId;
        _title = title;
        CoverImagePath = coverImagePath;
        InstallState = installState;
        OwnershipType = ownershipType;
        _tags = tags ?? Array.Empty<string>();
        _isPlaceholder = isPlaceholder;

        SaveUserDataCommand = new AsyncRelayCommand(SaveUserDataAsync, () => CanEditUserData && !_isSaving);
        UpdateStatusOptions(userData.Status);
        ApplyUserData(userData);
    }

    public static GameDetailsViewModel CreateEmpty(ILocalizationService localizationService, IGameUserDataService userDataService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        ArgumentNullException.ThrowIfNull(userDataService);
        return new GameDetailsViewModel(
            localizationService,
            userDataService,
            0,
            localizationService.GetString("GameDetails_NoSelectionTitle"),
            null,
            InstallState.Unknown,
            OwnershipType.Unknown,
            Array.Empty<string>(),
            GameUserData.Empty,
            true);
    }

    public static GameDetailsViewModel FromGame(
        GameEntry game,
        string? coverPath,
        ILocalizationService localizationService,
        IGameUserDataService userDataService)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(localizationService);
        ArgumentNullException.ThrowIfNull(userDataService);

        var tags = game.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>();

        return new GameDetailsViewModel(
            localizationService,
            userDataService,
            game.AppId,
            game.Title,
            coverPath,
            game.InstallState,
            game.OwnershipType,
            tags,
            game.UserData,
            false);
    }

    public uint AppId { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string? CoverImagePath { get; }

    public InstallState InstallState { get; }

    public OwnershipType OwnershipType { get; }

    public IReadOnlyList<string> Tags => _tags;

    public bool CanLaunch => InstallState == InstallState.Installed;

    public bool CanInstall => InstallState is InstallState.Available or InstallState.Shared;

    public IReadOnlyList<BacklogStatusOption> StatusOptions => _statusOptions;

    public BacklogStatusOption SelectedStatusOption
    {
        get => _selectedStatusOption;
        set
        {
            if (value is null)
            {
                return;
            }

            SetProperty(ref _selectedStatusOption, value);
        }
    }

    public string PersonalNotes
    {
        get => _personalNotes;
        set => SetProperty(ref _personalNotes, value ?? string.Empty);
    }

    public string PlaytimeHoursText
    {
        get => _playtimeHoursText;
        set => SetProperty(ref _playtimeHoursText, value ?? string.Empty);
    }

    public string TargetSessionHoursText
    {
        get => _targetSessionHoursText;
        set => SetProperty(ref _targetSessionHoursText, value ?? string.Empty);
    }

    public string EstimatedCompletionText
    {
        get => _estimatedCompletionText;
        private set => SetProperty(ref _estimatedCompletionText, value);
    }

    public string ProgressSummary
    {
        get => _progressSummary;
        private set => SetProperty(ref _progressSummary, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool CanEditUserData => !_isPlaceholder;

    public string InstallationStatus => InstallState switch
    {
        InstallState.Installed => _localizationService.GetString("GameDetails_InstallState_Installed"),
        InstallState.Available => OwnershipType == OwnershipType.FamilyShared
            ? _localizationService.GetString("GameDetails_InstallState_FamilySharing")
            : _localizationService.GetString("GameDetails_InstallState_Available"),
        InstallState.Shared => _localizationService.GetString("GameDetails_InstallState_FamilySharing"),
        InstallState.Unknown => _localizationService.GetString("GameDetails_InstallState_Unknown"),
        _ => _localizationService.GetString("GameDetails_InstallState_Unknown"),
    };

    public AsyncRelayCommand SaveUserDataCommand { get; }

    public void RefreshLocalization()
    {
        if (_isPlaceholder)
        {
            Title = _localizationService.GetString("GameDetails_NoSelectionTitle");
        }

        UpdateStatusOptions(_selectedStatusOption.Status);
        UpdateEstimatedCompletion(_userData.EstimatedCompletionTime);
        UpdateProgressSummary();
        OnPropertyChanged(nameof(InstallationStatus));
    }

    private void UpdateStatusOptions(BacklogStatus desiredStatus)
    {
        var options = Enum.GetValues<BacklogStatus>()
            .Select(status => new BacklogStatusOption(status, _localizationService.GetString($"BacklogStatus_{status}")))
            .ToList();

        _statusOptions = options;
        OnPropertyChanged(nameof(StatusOptions));

        var matching = options.FirstOrDefault(option => option.Status == desiredStatus) ?? options[0];
        _selectedStatusOption = matching;
        OnPropertyChanged(nameof(SelectedStatusOption));
    }

    private void ApplyUserData(GameUserData userData)
    {
        _userData = userData ?? GameUserData.Empty;
        PersonalNotes = _userData.Notes ?? string.Empty;
        PlaytimeHoursText = FormatHours(_userData.Playtime);
        TargetSessionHoursText = FormatHours(_userData.TargetSessionLength);
        UpdateEstimatedCompletion(_userData.EstimatedCompletionTime);
        UpdateProgressSummary();
        ErrorMessage = string.Empty;
    }

    private void UpdateEstimatedCompletion(TimeSpan? estimate)
    {
        EstimatedCompletionText = estimate.HasValue
            ? _localizationService.GetString("GameDetails_EstimatedCompletion", FormatHours(estimate))
            : _localizationService.GetString("GameDetails_EstimatedCompletionUnavailable");
    }

    private void UpdateProgressSummary()
    {
        var playtime = _userData.Playtime;
        var estimate = _userData.EstimatedCompletionTime;

        if (playtime.HasValue && estimate.HasValue && estimate.Value > TimeSpan.Zero)
        {
            var ratio = Math.Clamp(playtime.Value.TotalMinutes / estimate.Value.TotalMinutes, 0d, 1d);
            var percentage = Math.Round(ratio * 100, 0);
            ProgressSummary = _localizationService.GetString(
                "GameDetails_ProgressWithEstimate",
                FormatHours(playtime),
                FormatHours(estimate),
                percentage);
        }
        else if (playtime.HasValue)
        {
            ProgressSummary = _localizationService.GetString(
                "GameDetails_ProgressPlaytimeOnly",
                FormatHours(playtime));
        }
        else
        {
            ProgressSummary = _localizationService.GetString("GameDetails_ProgressUnavailable");
        }
    }

    private async Task SaveUserDataAsync()
    {
        if (!CanEditUserData)
        {
            return;
        }

        ErrorMessage = string.Empty;

        if (!TryParseHours(PlaytimeHoursText, out var playtime, out var error))
        {
            ErrorMessage = error ?? string.Empty;
            return;
        }

        if (!TryParseHours(TargetSessionHoursText, out var targetSession, out error))
        {
            ErrorMessage = error ?? string.Empty;
            return;
        }

        var updated = _userData with
        {
            Status = _selectedStatusOption.Status,
            Notes = PersonalNotes?.Trim() ?? string.Empty,
            Playtime = playtime,
            TargetSessionLength = targetSession,
        };

        try
        {
            _isSaving = true;
            SaveUserDataCommand.RaiseCanExecuteChanged();
            var persisted = await _userDataService.SaveAsync(AppId, updated).ConfigureAwait(true);
            UpdateStatusOptions(persisted.Status);
            ApplyUserData(persisted);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _isSaving = false;
            SaveUserDataCommand.RaiseCanExecuteChanged();
        }
    }

    private bool TryParseHours(string text, out TimeSpan? value, out string? error)
    {
        error = null;
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var hours) && hours >= 0)
        {
            value = TimeSpan.FromHours(hours);
            return true;
        }

        error = _localizationService.GetString("GameDetails_InvalidHours");
        return false;
    }

    private static string FormatHours(TimeSpan? value)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        return value.Value.TotalHours.ToString("0.##", CultureInfo.CurrentCulture);
    }

    public sealed record BacklogStatusOption(BacklogStatus Status, string DisplayName);
}
