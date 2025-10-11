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

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public SelectionEngine(string? settingsPath = null, Func<DateTimeOffset>? clock = null)
    {
        _settingsPath = settingsPath ?? BuildDefaultSettingsPath();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _state = LoadSettings();
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
        var requiredTags = filters.RequiredTags;
        var results = new List<GameEntry>();

        foreach (var game in games)
        {
            if (!skipExclusionCheck && excludedIds.Contains(game.AppId))
            {
                continue;
            }

            if (filters.RequireInstalled && game.InstallState != InstallState.Installed)
            {
                continue;
            }

            if (!filters.IncludeFamilyShared && game.OwnershipType == OwnershipType.FamilyShared)
            {
                continue;
            }

            if (requiredTags.Count > 0)
            {
                var tags = game.Tags ?? Array.Empty<string>();
                var hasAllTags = requiredTags.All(tag => tags.Any(gameTag => string.Equals(gameTag, tag, StringComparison.OrdinalIgnoreCase)));
                if (!hasAllTags)
                {
                    continue;
                }
            }

            if (filters.MinimumSizeOnDisk is long minimum)
            {
                if (game.SizeOnDisk is not long size || size < minimum)
                {
                    continue;
                }
            }

            if (filters.MaximumSizeOnDisk is long maximum)
            {
                if (game.SizeOnDisk is not long size || size > maximum)
                {
                    continue;
                }
            }

            results.Add(game);
        }

        return results;
    }

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

    private GameEntry ChooseGame(IReadOnlyList<GameEntry> candidates)
    {
        var weights = new double[candidates.Count];
        double total = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            weights[i] = Math.Max(0, GetWeight(candidates[i]));
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

    private double GetWeight(GameEntry game)
    {
        if (_state.Preferences.GameWeights.TryGetValue(game.AppId, out var weight))
        {
            return weight;
        }

        return 1d;
    }

    private double NextRandom()
    {
        if (_state.Preferences.Seed is int seed)
        {
            var random = new Random(seed);
            for (var i = 0; i < _state.RandomPosition; i++)
            {
                _ = random.NextDouble();
            }

            var value = random.NextDouble();
            _state.RandomPosition++;
            return value;
        }

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
}
