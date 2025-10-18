using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Domain;

namespace Domain.Selection;

public sealed class SelectionEngine : ISelectionEngine
{
    private readonly string _settingsPath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _syncRoot = new();
    private SelectionSettings _state;
    private static readonly HashSet<uint> EmptyExcludedIds = new();
    private Random? _seededRandom;
    private int? _seededRandomSeed;
    private int _seededRandomPosition;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public SelectionEngine(string? settingsPath = null, Func<DateTimeOffset>? clock = null)
    {
        _settingsPath = settingsPath ?? BuildDefaultSettingsPath();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _state = LoadSettings();
        RefreshSeededRandomFromState();
    }

    public SelectionPreferences GetPreferences()
    {
        lock (_syncRoot)
        {
            return _state.Preferences.Clone();
        }
    }

    public void UpdatePreferences(SelectionPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        lock (_syncRoot)
        {
            var cloned = preferences.Clone();
            cloned.Normalize();

            var previousSeed = _state.Preferences.Seed;
            _state.Preferences = cloned;
            if (previousSeed != cloned.Seed)
            {
                _state.RandomPosition = 0;
                RefreshSeededRandomFromState();
            }

            TrimHistory();
            SaveSettings();
        }
    }

    public IReadOnlyList<SelectionHistoryEntry> GetHistory()
    {
        lock (_syncRoot)
        {
            return _state.History
                .Select(entry => new SelectionHistoryEntry
                {
                    AppId = entry.AppId,
                    Title = entry.Title,
                    SelectedAt = entry.SelectedAt,
                })
                .ToList();
        }
    }

    public void ClearHistory()
    {
        lock (_syncRoot)
        {
            _state.History.Clear();
            SaveSettings();
        }
    }

    public GameUserData GetUserData(uint appId)
    {
        lock (_syncRoot)
        {
            if (_state.UserData.TryGetValue(appId, out var data))
            {
                return data;
            }

            return new GameUserData();
        }
    }

    public IReadOnlyDictionary<uint, GameUserData> GetUserDataSnapshot()
    {
        lock (_syncRoot)
        {
            return new Dictionary<uint, GameUserData>(_state.UserData);
        }
    }

    public void UpdateUserData(uint appId, GameUserData userData)
    {
        ArgumentNullException.ThrowIfNull(userData);

        lock (_syncRoot)
        {
            var normalized = userData.Normalize();
            var changed = false;
            if (IsUserDataEmpty(normalized))
            {
                changed = _state.UserData.Remove(appId);
            }
            else if (!_state.UserData.TryGetValue(appId, out var existing) || existing != normalized)
            {
                _state.UserData[appId] = normalized;
                changed = true;
            }

            if (changed)
            {
                SaveSettings();
            }
        }
    }

    public GameEntry PickNext(IEnumerable<GameEntry> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        lock (_syncRoot)
        {
            var candidates = ApplyFilters(games);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("No games available after applying the current selection filters.");
            }

            var selected = ChooseGame(candidates);
            RegisterSelection(selected);
            SaveSettings();
            return selected;
        }
    }

    public IReadOnlyList<GameEntry> FilterGames(IEnumerable<GameEntry> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        lock (_syncRoot)
        {
            return ApplyFilters(games);
        }
    }

    private SelectionSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                using var stream = File.OpenRead(_settingsPath);
                var loaded = JsonSerializer.Deserialize<SelectionSettings>(stream, SerializerOptions);
                if (loaded is not null)
                {
                    NormalizeState(loaded);
                    return loaded;
                }
            }
        }
        catch (IOException)
        {
            // If reading fails, fall back to defaults.
        }
        catch (JsonException)
        {
            // Ignore invalid JSON and rebuild defaults.
        }

        var defaults = new SelectionSettings();
        NormalizeState(defaults);
        return defaults;
    }

    private void SaveSettings()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(_settingsPath);
        JsonSerializer.Serialize(stream, _state, SerializerOptions);
    }

    private static string BuildDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Path.GetTempPath();
        }

        return Path.Combine(appData, "SteamBacklogPicker", "settings.json");
    }

    private void NormalizeState(SelectionSettings settings)
    {
        settings.Preferences ??= new SelectionPreferences();
        settings.Preferences.Normalize();
        settings.History ??= new List<SelectionHistoryEntry>();
        foreach (var entry in settings.History)
        {
            entry.Title ??= string.Empty;
        }
        if (settings.RandomPosition < 0)
        {
            settings.RandomPosition = 0;
        }

        settings.UserData ??= new Dictionary<uint, GameUserData>();
        var toRemove = new List<uint>();
        foreach (var (appId, data) in settings.UserData.ToArray())
        {
            var normalized = data.Normalize();
            if (IsUserDataEmpty(normalized))
            {
                toRemove.Add(appId);
                continue;
            }

            settings.UserData[appId] = normalized;
        }

        foreach (var appId in toRemove)
        {
            settings.UserData.Remove(appId);
        }

        TrimHistory(settings);
    }

    private void TrimHistory()
    {
        TrimHistory(_state);
    }

    private void TrimHistory(SelectionSettings settings)
    {
        var limit = Math.Max(0, settings.Preferences.HistoryLimit);
        if (limit == 0)
        {
            settings.History.Clear();
            return;
        }

        if (settings.History.Count > limit)
        {
            settings.History.RemoveRange(0, settings.History.Count - limit);
        }
    }

    private List<GameEntry> ApplyFilters(IEnumerable<GameEntry> games)
    {
        var filters = _state.Preferences.Filters;
        var excludedIds = GetExcludedAppIds();
        var skipExclusionCheck = excludedIds.Count == 0;
        var allowedCategories = filters.IncludedCategories;
        var filterByCategory = allowedCategories.Count > 0;
        var allowedCategorySet = filterByCategory
            ? new HashSet<ProductCategory>(allowedCategories)
            : null;
        var requiredCollection = filters.RequiredCollection;
        var filterByCollection = !string.IsNullOrWhiteSpace(requiredCollection);
        var allowedCompatibility = filters.AllowedDeckCompatibility;
        var allowedStatuses = filters.AllowedBacklogStatuses;
        var filterByStatus = allowedStatuses != BacklogStatusFilter.All;
        var requireSinglePlayer = filters.RequireSinglePlayer;
        var requireMultiplayer = filters.RequireMultiplayer;
        var requireVr = filters.RequireVr;
        var moodTags = filters.MoodTags ?? new List<string>();
        var filterByMood = moodTags.Count > 0;
        var minimumPlaytime = filters.MinimumPlaytime;
        var maximumTargetSessionLength = filters.MaximumTargetSessionLength;
        var maximumEstimatedCompletionTime = filters.MaximumEstimatedCompletionTime;
        var results = new List<GameEntry>();

        foreach (var game in games)
        {
            if (!skipExclusionCheck && excludedIds.Contains(game.AppId))
            {
                continue;
            }

            var enriched = AttachUserData(game);
            var userData = enriched.UserData;

            if (filters.RequireInstalled && enriched.InstallState is not (InstallState.Installed or InstallState.Shared))
            {
                continue;
            }

            if (filterByStatus && !IsStatusAllowed(userData.Status, allowedStatuses))
            {
                continue;
            }

            if (requireSinglePlayer && !GameEntryCapabilities.SupportsSinglePlayer(enriched))
            {
                continue;
            }

            if (requireMultiplayer && !GameEntryCapabilities.SupportsMultiplayer(enriched))
            {
                continue;
            }

            if (requireVr && !GameEntryCapabilities.SupportsVirtualReality(enriched))
            {
                continue;
            }

            if (minimumPlaytime is { } minimum && (!userData.Playtime.HasValue || userData.Playtime.Value < minimum))
            {
                continue;
            }

            if (maximumTargetSessionLength is { } maxSession && userData.TargetSessionLength is { } sessionLength && sessionLength > maxSession)
            {
                continue;
            }

            if (maximumEstimatedCompletionTime is { } maxEstimate && userData.EstimatedCompletionTime is { } estimate && estimate > maxEstimate)
            {
                continue;
            }

            var compatibility = GetCompatibilityFlag(enriched.DeckCompatibility);
            if ((allowedCompatibility & compatibility) == 0)
            {
                continue;
            }

            var category = enriched.ProductCategory;
            if (category == ProductCategory.Unknown)
            {
                category = ProductCategory.Game;
            }

            if (allowedCategorySet is not null && !allowedCategorySet.Contains(category))
            {
                continue;
            }

            if (filterByCollection)
            {
                var tags = enriched.Tags;
                if (tags is null || !tags.Any(tag => string.Equals(tag, requiredCollection, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            if (filterByMood && !GameEntryCapabilities.MatchesMoodTags(enriched, moodTags))
            {
                continue;
            }

            results.Add(enriched);
        }

        return results;
    }

    private static DeckCompatibilityFilter GetCompatibilityFlag(SteamDeckCompatibility compatibility) => compatibility switch
    {
        SteamDeckCompatibility.Verified => DeckCompatibilityFilter.Verified,
        SteamDeckCompatibility.Playable => DeckCompatibilityFilter.Playable,
        SteamDeckCompatibility.Unsupported => DeckCompatibilityFilter.Unsupported,
        _ => DeckCompatibilityFilter.Unknown,
    };

    private HashSet<uint> GetExcludedAppIds()
    {
        var toExclude = Math.Min(_state.Preferences.RecentGameExclusionCount, _state.History.Count);
        if (toExclude <= 0)
        {
            return EmptyExcludedIds;
        }

        var set = new HashSet<uint>();
        for (var i = _state.History.Count - toExclude; i < _state.History.Count; i++)
        {
            set.Add(_state.History[i].AppId);
        }

        return set;
    }

    private GameEntry AttachUserData(GameEntry game)
    {
        if (_state.UserData.TryGetValue(game.AppId, out var persisted))
        {
            return game with { UserData = persisted };
        }

        var provided = game.UserData.Normalize();
        if (IsUserDataEmpty(provided))
        {
            return game with { UserData = new GameUserData() };
        }

        return game with { UserData = provided };
    }

    private static bool IsStatusAllowed(BacklogStatus status, BacklogStatusFilter allowed)
    {
        var flag = status switch
        {
            BacklogStatus.Wishlist => BacklogStatusFilter.Wishlist,
            BacklogStatus.Backlog => BacklogStatusFilter.Backlog,
            BacklogStatus.Playing => BacklogStatusFilter.Playing,
            BacklogStatus.Completed => BacklogStatusFilter.Completed,
            BacklogStatus.Shelved => BacklogStatusFilter.Shelved,
            BacklogStatus.Abandoned => BacklogStatusFilter.Abandoned,
            _ => BacklogStatusFilter.Uncategorized,
        };

        return (allowed & flag) != 0;
    }

    private static bool IsUserDataEmpty(GameUserData data) =>
        data.Status == BacklogStatus.Uncategorized
        && string.IsNullOrWhiteSpace(data.Notes)
        && data.Playtime is null
        && data.TargetSessionLength is null
        && data.EstimatedCompletionTime is null
        && data.EstimatedCompletionTimeUpdatedAt is null;

    private GameEntry ChooseGame(IReadOnlyList<GameEntry> candidates)
    {
        var weights = new double[candidates.Count];
        double total = 0;
        var referenceTime = _clock();
        for (var i = 0; i < candidates.Count; i++)
        {
            weights[i] = Math.Max(0, GetWeight(candidates[i], referenceTime));
            total += weights[i];
        }

        if (total <= 0)
        {
            var value = NextRandom();
            var index = (int)Math.Floor(value * candidates.Count);
            if (index >= candidates.Count)
            {
                index = candidates.Count - 1;
            }

            return candidates[index];
        }

        var threshold = NextRandom() * total;
        var cumulative = 0d;
        for (var i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (threshold <= cumulative || i == candidates.Count - 1)
            {
                return candidates[i];
            }
        }

        return candidates[^1];
    }

    private double GetWeight(GameEntry game, DateTimeOffset referenceTime)
    {
        var filters = _state.Preferences.Filters ?? new SelectionFilters();
        var weight = 1d;

        weight *= ComputeInstallStateMultiplier(game, filters.InstallStateWeight);
        weight *= ComputeLastPlayedMultiplier(game, referenceTime, filters.LastPlayedRecencyWeight);
        weight *= ComputeDeckCompatibilityMultiplier(game, filters.DeckCompatibilityWeight);

        return weight;
    }

    private static double ComputeInstallStateMultiplier(GameEntry game, double sliderValue)
    {
        var normalized = NormalizeSlider(sliderValue);
        var bias = normalized - 1d;
        var isInstalled = game.InstallState is InstallState.Installed or InstallState.Shared;
        var multiplier = isInstalled ? 1d + bias : 1d - bias;
        return Math.Clamp(multiplier, 0d, 2d);
    }

    private static double ComputeLastPlayedMultiplier(GameEntry game, DateTimeOffset referenceTime, double sliderValue)
    {
        var normalized = NormalizeSlider(sliderValue);
        var bias = normalized - 1d;
        if (Math.Abs(bias) < 0.0001d)
        {
            return 1d;
        }

        var recencyScore = GetRecencyScore(game, referenceTime);
        var recencyNormalized = (recencyScore * 2d) - 1d;
        var multiplier = 1d + bias * recencyNormalized;
        return Math.Clamp(multiplier, 0d, 2d);
    }

    private static double GetRecencyScore(GameEntry game, DateTimeOffset referenceTime)
    {
        if (game.LastPlayed is null)
        {
            return 1d;
        }

        var lastPlayed = game.LastPlayed.Value;
        if (lastPlayed >= referenceTime)
        {
            return 0d;
        }

        var days = (referenceTime - lastPlayed).TotalDays;
        const double maxDays = 180d;
        if (days >= maxDays)
        {
            return 1d;
        }

        if (days <= 0)
        {
            return 0d;
        }

        return Math.Clamp(days / maxDays, 0d, 1d);
    }

    private static double ComputeDeckCompatibilityMultiplier(GameEntry game, double sliderValue)
    {
        var normalized = NormalizeSlider(sliderValue);
        var bias = normalized - 1d;
        if (Math.Abs(bias) < 0.0001d)
        {
            return 1d;
        }

        var compatibilityScore = game.DeckCompatibility switch
        {
            SteamDeckCompatibility.Verified => 1d,
            SteamDeckCompatibility.Playable => 0.75d,
            SteamDeckCompatibility.Unsupported => 0d,
            _ => 0.5d,
        };

        var compatibilityNormalized = (compatibilityScore * 2d) - 1d;
        var multiplier = 1d + bias * compatibilityNormalized;
        return Math.Clamp(multiplier, 0d, 2d);
    }

    private static double NormalizeSlider(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1d;
        }

        return Math.Clamp(value, 0d, 2d);
    }

    private double NextRandom()
    {
        if (_state.Preferences.Seed is int seed)
        {
            EnsureSeededRandomInitialized(seed);

            var value = _seededRandom!.NextDouble();
            _seededRandomPosition++;
            _state.RandomPosition = _seededRandomPosition;
            return value;
        }

        ResetSeededRandom();
        return Random.Shared.NextDouble();
    }

    private void RegisterSelection(GameEntry selected)
    {
        var limit = Math.Max(0, _state.Preferences.HistoryLimit);
        if (limit == 0)
        {
            _state.History.Clear();
        }
        else
        {
            while (_state.History.Count >= limit)
            {
                _state.History.RemoveAt(0);
            }
        }

        if (limit > 0)
        {
            _state.History.Add(new SelectionHistoryEntry
            {
                AppId = selected.AppId,
                Title = selected.Title,
                SelectedAt = _clock(),
            });
        }
    }

    private void EnsureSeededRandomInitialized(int seed)
    {
        if (_seededRandom is null || _seededRandomSeed != seed)
        {
            RefreshSeededRandomFromState();
        }
        else if (_seededRandomPosition < _state.RandomPosition)
        {
            AdvanceSeededRandomTo(_state.RandomPosition);
        }
        else if (_seededRandomPosition > _state.RandomPosition)
        {
            _seededRandom = new Random(seed);
            _seededRandomSeed = seed;
            _seededRandomPosition = 0;
            AdvanceSeededRandomTo(_state.RandomPosition);
        }
    }

    private void RefreshSeededRandomFromState()
    {
        if (_state.Preferences.Seed is int seed)
        {
            _seededRandom = new Random(seed);
            _seededRandomSeed = seed;
            _seededRandomPosition = 0;
            AdvanceSeededRandomTo(_state.RandomPosition);
        }
        else
        {
            ResetSeededRandom();
        }
    }

    private void AdvanceSeededRandomTo(int targetPosition)
    {
        if (_seededRandom is null)
        {
            return;
        }

        if (targetPosition < 0)
        {
            targetPosition = 0;
        }

        while (_seededRandomPosition < targetPosition)
        {
            _seededRandom.NextDouble();
            _seededRandomPosition++;
        }
    }

    private void ResetSeededRandom()
    {
        _seededRandom = null;
        _seededRandomSeed = null;
        _seededRandomPosition = 0;
    }
}
