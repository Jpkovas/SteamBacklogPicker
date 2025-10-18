using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Domain;
using Domain.Selection;
using SteamBacklogPicker.UI.Services;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class GameDetailsViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private readonly ISelectionEngine? _selectionEngine;
    private readonly IGameCompletionEstimator? _completionEstimator;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _isPlaceholder;
    private readonly List<BacklogStatusOption> _statusOptions = new();
    private readonly TimeSpan _estimateCacheLifetime = TimeSpan.FromDays(30);
    private string _title;
    private BacklogStatus _status;
    private string _notes = string.Empty;
    private string _playtimeText = string.Empty;
    private TimeSpan? _playtime;
    private bool _hasTargetSession;
    private double _targetSessionMinutes;
    private TimeSpan? _estimatedCompletionTime;
    private DateTimeOffset? _estimatedCompletionUpdatedAt;
    private bool _isRefreshingEstimate;
    private string _estimateError = string.Empty;
    private GameUserData _currentUserData = new();
    private bool _isUpdatingUserData;

    private GameDetailsViewModel(
        ILocalizationService localizationService,
        ISelectionEngine? selectionEngine,
        IGameCompletionEstimator? completionEstimator,
        INetworkStatusService? networkStatusService,
        Func<DateTimeOffset> clock,
        uint appId,
        string title,
        string? coverImagePath,
        InstallState installState,
        OwnershipType ownershipType,
        IReadOnlyList<string> tags,
        bool isPlaceholder,
        GameUserData userData)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _selectionEngine = selectionEngine;
        _completionEstimator = completionEstimator;
        _networkStatusService = networkStatusService;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _isPlaceholder = isPlaceholder;
        AppId = appId;
        _title = title;
        CoverImagePath = coverImagePath;
        InstallState = installState;
        OwnershipType = ownershipType;
        Tags = tags;

        UpdateStatusOptions();
        ApplyUserData(userData);

        RefreshEstimateCommand = new AsyncRelayCommand(RefreshEstimateAsync, () => CanRefreshEstimate);
    }

    public static GameDetailsViewModel CreateEmpty(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        return new GameDetailsViewModel(
            localizationService,
            null,
            null,
            null,
            () => DateTimeOffset.UtcNow,
            0,
            localizationService.GetString("GameDetails_NoSelectionTitle"),
            null,
            InstallState.Unknown,
            OwnershipType.Unknown,
            Array.Empty<string>(),
            true,
            new GameUserData());
    }

    public static GameDetailsViewModel FromGame(
        GameEntry game,
        string? coverPath,
        ILocalizationService localizationService,
        ISelectionEngine selectionEngine,
        IGameCompletionEstimator completionEstimator,
        INetworkStatusService networkStatusService,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(localizationService);
        ArgumentNullException.ThrowIfNull(selectionEngine);
        ArgumentNullException.ThrowIfNull(completionEstimator);
        ArgumentNullException.ThrowIfNull(networkStatusService);

        var tags = game.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                   ?? Array.Empty<string>();
        var effectiveClock = clock ?? (() => DateTimeOffset.UtcNow);
        return new GameDetailsViewModel(
            localizationService,
            selectionEngine,
            completionEstimator,
            networkStatusService,
            effectiveClock,
            game.AppId,
            game.Title,
            coverPath,
            game.InstallState,
            game.OwnershipType,
            tags,
            false,
            game.UserData);
    }

    public event EventHandler<GameUserData>? UserDataChanged;

    public uint AppId { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string? CoverImagePath { get; }

    public InstallState InstallState { get; }

    public OwnershipType OwnershipType { get; }

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<BacklogStatusOption> StatusOptions => _statusOptions;

    public BacklogStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value))
            {
                return;
            }

            if (ShouldIgnoreUserUpdates())
            {
                return;
            }

            PersistUserData();
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (!SetProperty(ref _notes, normalized))
            {
                return;
            }

            if (ShouldIgnoreUserUpdates())
            {
                return;
            }

            PersistUserData();
        }
    }

    public string PlaytimeText
    {
        get => _playtimeText;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (!TryParseHours(normalized, out var parsed))
            {
                return;
            }

            if (!SetProperty(ref _playtimeText, normalized))
            {
                return;
            }

            _playtime = parsed;
            OnPropertyChanged(nameof(CompletionProgress));
            OnPropertyChanged(nameof(CompletionSummary));

            if (ShouldIgnoreUserUpdates())
            {
                return;
            }

            PersistUserData();
        }
    }

    public bool HasTargetSession
    {
        get => _hasTargetSession;
        set
        {
            if (!SetProperty(ref _hasTargetSession, value))
            {
                return;
            }

            if (ShouldIgnoreUserUpdates())
            {
                return;
            }

            if (!value)
            {
                _isUpdatingUserData = true;
                TargetSessionMinutes = 0;
                _isUpdatingUserData = false;
            }

            PersistUserData();
        }
    }

    public double TargetSessionMinutes
    {
        get => _targetSessionMinutes;
        set
        {
            var normalized = Math.Clamp(value, 0d, 600d);
            if (!SetProperty(ref _targetSessionMinutes, normalized))
            {
                return;
            }

            if (normalized > 0 && !_hasTargetSession)
            {
                _hasTargetSession = true;
                OnPropertyChanged(nameof(HasTargetSession));
            }

            OnPropertyChanged(nameof(TargetSessionDisplay));

            if (ShouldIgnoreUserUpdates())
            {
                return;
            }

            PersistUserData();
        }
    }

    public string TargetSessionDisplay
    {
        get
        {
            if (!_hasTargetSession || _targetSessionMinutes <= 0)
            {
                return _localizationService.GetString("GameDetails_TargetSession_Unset");
            }

            var minutes = Math.Round(_targetSessionMinutes);
            return _localizationService.GetString("GameDetails_TargetSession_Value", minutes);
        }
    }

    public TimeSpan? EstimatedCompletionTime
    {
        get => _estimatedCompletionTime;
        private set
        {
            if (!SetProperty(ref _estimatedCompletionTime, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasEstimatedCompletion));
            OnPropertyChanged(nameof(EstimatedCompletionDisplay));
            OnPropertyChanged(nameof(CompletionProgress));
            OnPropertyChanged(nameof(CompletionSummary));
            OnPropertyChanged(nameof(EstimatedCompletionUpdatedDisplay));
        }
    }

    public bool HasEstimatedCompletion => EstimatedCompletionTime is not null;

    public string EstimatedCompletionDisplay => FormatDuration(EstimatedCompletionTime);

    public string EstimatedCompletionUpdatedDisplay
    {
        get
        {
            if (_estimatedCompletionUpdatedAt is null)
            {
                return string.Empty;
            }

            var localTime = _estimatedCompletionUpdatedAt.Value.ToLocalTime();
            return _localizationService.GetString("GameDetails_EstimateUpdated", localTime.ToString("g", CultureInfo.CurrentCulture));
        }
    }

    public double CompletionProgress
    {
        get
        {
            if (_playtime is null || _estimatedCompletionTime is null || _estimatedCompletionTime.Value.TotalSeconds <= 0)
            {
                return 0d;
            }

            var ratio = _playtime.Value.TotalSeconds / _estimatedCompletionTime.Value.TotalSeconds;
            return Math.Clamp(ratio, 0d, 1d);
        }
    }

    public string CompletionSummary
    {
        get
        {
            var played = FormatDuration(_playtime);
            var estimate = FormatDuration(_estimatedCompletionTime);
            return _localizationService.GetString("GameDetails_CompletionSummary", played, estimate);
        }
    }

    public string EstimateError
    {
        get => _estimateError;
        private set => SetProperty(ref _estimateError, value);
    }

    public bool CanLaunch => InstallState == InstallState.Installed;

    public bool CanInstall => InstallState is InstallState.Available or InstallState.Shared;

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

    public bool CanRefreshEstimate => !_isPlaceholder && _completionEstimator is not null && (_networkStatusService?.IsOffline() != true);

    public bool IsRefreshingEstimate
    {
        get => _isRefreshingEstimate;
        private set
        {
            if (SetProperty(ref _isRefreshingEstimate, value))
            {
                RefreshEstimateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand RefreshEstimateCommand { get; }

    public bool IsOffline => _networkStatusService?.IsOffline() ?? false;

    public bool CanLaunchOrInstall => CanLaunch || CanInstall;

    public bool HasSelection => !_isPlaceholder;

    public void RefreshLocalization()
    {
        if (_isPlaceholder)
        {
            Title = _localizationService.GetString("GameDetails_NoSelectionTitle");
        }

        UpdateStatusOptions();
        OnPropertyChanged(nameof(InstallationStatus));
        OnPropertyChanged(nameof(TargetSessionDisplay));
        OnPropertyChanged(nameof(EstimatedCompletionDisplay));
        OnPropertyChanged(nameof(EstimatedCompletionUpdatedDisplay));
        OnPropertyChanged(nameof(CompletionSummary));
        RefreshEstimateCommand.RaiseCanExecuteChanged();
    }

    public async Task EnsureEstimateAsync()
    {
        if (!ShouldRefreshEstimate())
        {
            return;
        }

        await RefreshEstimateAsync().ConfigureAwait(true);
    }

    private bool ShouldRefreshEstimate()
    {
        if (_isPlaceholder || _completionEstimator is null)
        {
            return false;
        }

        if (_networkStatusService?.IsOffline() == true)
        {
            return false;
        }

        if (_estimatedCompletionTime is null)
        {
            return true;
        }

        if (_estimatedCompletionUpdatedAt is null)
        {
            return true;
        }

        return _clock() - _estimatedCompletionUpdatedAt > _estimateCacheLifetime;
    }

    private async Task RefreshEstimateAsync()
    {
        if (_isPlaceholder || _completionEstimator is null)
        {
            return;
        }

        if (_networkStatusService?.IsOffline() == true)
        {
            EstimateError = _localizationService.GetString("GameDetails_Estimate_Offline");
            return;
        }

        try
        {
            IsRefreshingEstimate = true;
            EstimateError = string.Empty;
            var estimate = await _completionEstimator.GetEstimatedCompletionAsync(Title, default).ConfigureAwait(true);
            if (estimate is not null)
            {
                _estimatedCompletionUpdatedAt = _clock();
                EstimatedCompletionTime = estimate;
                PersistUserData();
            }
        }
        catch (Exception ex)
        {
            EstimateError = ex.Message;
        }
        finally
        {
            IsRefreshingEstimate = false;
        }
    }

    private void ApplyUserData(GameUserData data)
    {
        _isUpdatingUserData = true;
        _currentUserData = data.Normalize();
        _status = _currentUserData.Status;
        _notes = _currentUserData.Notes ?? string.Empty;
        _playtime = _currentUserData.Playtime;
        _playtimeText = FormatHours(_playtime);
        _hasTargetSession = _currentUserData.TargetSessionLength is { } target && target > TimeSpan.Zero;
        _targetSessionMinutes = _hasTargetSession ? Math.Round(_currentUserData.TargetSessionLength!.Value.TotalMinutes) : 0;
        _estimatedCompletionTime = _currentUserData.EstimatedCompletionTime;
        _estimatedCompletionUpdatedAt = _currentUserData.EstimatedCompletionTimeUpdatedAt;
        _isUpdatingUserData = false;

        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(PlaytimeText));
        OnPropertyChanged(nameof(HasTargetSession));
        OnPropertyChanged(nameof(TargetSessionMinutes));
        OnPropertyChanged(nameof(TargetSessionDisplay));
        OnPropertyChanged(nameof(EstimatedCompletionTime));
        OnPropertyChanged(nameof(HasEstimatedCompletion));
        OnPropertyChanged(nameof(EstimatedCompletionDisplay));
        OnPropertyChanged(nameof(EstimatedCompletionUpdatedDisplay));
        OnPropertyChanged(nameof(CompletionProgress));
        OnPropertyChanged(nameof(CompletionSummary));
    }

    private void PersistUserData()
    {
        var updated = BuildUserData();
        _currentUserData = updated;

        if (_selectionEngine is not null)
        {
            _selectionEngine.UpdateUserData(AppId, updated);
        }

        UserDataChanged?.Invoke(this, updated);
    }

    private GameUserData BuildUserData()
    {
        TimeSpan? target = null;
        if (_hasTargetSession && _targetSessionMinutes > 0)
        {
            target = TimeSpan.FromMinutes(Math.Round(_targetSessionMinutes));
        }

        return new GameUserData
        {
            Status = _status,
            Notes = _notes,
            Playtime = _playtime,
            TargetSessionLength = target,
            EstimatedCompletionTime = _estimatedCompletionTime,
            EstimatedCompletionTimeUpdatedAt = _estimatedCompletionUpdatedAt,
        };
    }

    private bool ShouldIgnoreUserUpdates() => _isPlaceholder || _isUpdatingUserData;

    private void UpdateStatusOptions()
    {
        _statusOptions.Clear();
        _statusOptions.Add(new BacklogStatusOption(BacklogStatus.Uncategorized, _localizationService.GetString("BacklogStatus_Uncategorized")));
        _statusOptions.Add(new BacklogStatusOption(BacklogStatus.Wishlist, _localizationService.GetString("BacklogStatus_Wishlist")));
        _statusOptions.Add(new BacklogStatusOption(BacklogStatus.Backlog, _localizationService.GetString("BacklogStatus_Backlog")));
        _statusOptions.Add(new BacklogStatusOption(BacklogStatus.Playing, _localizationService.GetString("BacklogStatus_Playing")));
        _statusOptions.Add(new BacklogStatusOption(BacklogStatus.Completed, _localizationService.GetString("BacklogStatus_Completed")));
        _statusOptions.Add(new BacklogStatusOption(BacklogStatus.Shelved, _localizationService.GetString("BacklogStatus_Shelved")));
        _statusOptions.Add(new BacklogStatusOption(BacklogStatus.Abandoned, _localizationService.GetString("BacklogStatus_Abandoned")));
        OnPropertyChanged(nameof(StatusOptions));
    }

    private static bool TryParseHours(string text, out TimeSpan? result)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            result = null;
            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var hours))
        {
            result = null;
            return false;
        }

        if (double.IsNaN(hours) || double.IsInfinity(hours))
        {
            result = null;
            return false;
        }

        hours = Math.Max(0d, hours);
        result = TimeSpan.FromHours(hours);
        return true;
    }

    private string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return _localizationService.GetString("GameDetails_Duration_Unknown");
        }

        if (duration.Value.TotalHours >= 1)
        {
            var hours = duration.Value.TotalHours;
            return _localizationService.GetString("GameDetails_Duration_Hours", hours.ToString("0.#", CultureInfo.CurrentCulture));
        }

        var minutes = Math.Round(duration.Value.TotalMinutes);
        return _localizationService.GetString("GameDetails_Duration_Minutes", minutes);
    }

    private static string FormatHours(TimeSpan? duration)
    {
        if (duration is null)
        {
            return string.Empty;
        }

        return duration.Value.TotalHours.ToString("0.#", CultureInfo.CurrentCulture);
    }

    public sealed record class BacklogStatusOption(BacklogStatus Status, string DisplayName);
}
