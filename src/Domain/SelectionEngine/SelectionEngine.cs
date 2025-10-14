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
        var (destinationPath, isSymbolicLink) = GetSettingsDestinationPath();

        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        }

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), Path.GetRandomFileName());

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, _state, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                if (isSymbolicLink)
                {
                    if (File.Exists(destinationPath))
                    {
                        File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, destinationPath, overwrite: true);
                    }
                }
                else
                {
                    File.Move(tempPath, destinationPath, overwrite: true);
                }
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private (string DestinationPath, bool IsSymbolicLink) GetSettingsDestinationPath()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var attributes = File.GetAttributes(_settingsPath);
                if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    var targetInfo = File.ResolveLinkTarget(_settingsPath, returnFinalTarget: true);
                    if (targetInfo is FileInfo fileTarget)
                    {
                        return (fileTarget.FullName, true);
                    }

                    if (targetInfo is DirectoryInfo directoryTarget)
                    {
                        var fileName = Path.GetFileName(_settingsPath);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            return (Path.Combine(directoryTarget.FullName, fileName), true);
                        }
                    }
                }
            }
        }
        catch (IOException)
        {
            // Fall back to writing through the link if resolution fails.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore and fall back to the original path.
        }

        return (_settingsPath, false);
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Suppress cleanup failures.
        }
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
        var allowedCategories = filters.IncludedCategories ?? new List<ProductCategory> { ProductCategory.Game };
        var filterByCategory = allowedCategories.Count > 0;
        var requiredCollection = filters.RequiredCollection;
        var filterByCollection = !string.IsNullOrWhiteSpace(requiredCollection);
        var results = new List<GameEntry>();

        foreach (var game in games)
        {
            if (!skipExclusionCheck && excludedIds.Contains(game.AppId))
            {
                continue;
            }

            if (filters.RequireInstalled && game.InstallState is not (InstallState.Installed or InstallState.Shared))
            {
                continue;
            }

            var category = game.ProductCategory;
            if (category == ProductCategory.Unknown)
            {
                category = ProductCategory.Game;
            }

            if (filterByCategory && !allowedCategories.Contains(category))
            {
                continue;
            }

            if (filterByCollection)
            {
                var tags = game.Tags;
                if (tags is null || !tags.Any(tag => string.Equals(tag, requiredCollection, StringComparison.OrdinalIgnoreCase)))
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

    private static double GetWeight(GameEntry game) => 1d;

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
