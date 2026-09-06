using System.Globalization;
using System.IO;
using System.Linq;
using Domain;
using SteamClientAdapter;
using ValveFormatParser;

namespace SteamDiscovery;

public sealed class SteamAppManifestCache : IDisposable
{
    private readonly ISteamLibraryLocator _libraryLocator;
    private readonly ISteamClientAdapter _clientAdapter;
    private readonly ISteamVdfFallback _fallback;
    private readonly ValveTextVdfParser _parser;
    private readonly IPathComparisonStrategy _pathComparison;
    private readonly object _syncRoot = new();
    private readonly Dictionary<GameIdentifier, GameEntry> _entries = new();
    private readonly Dictionary<GameIdentifier, string> _manifestPathById = new();
    private readonly Dictionary<string, GameIdentifier> _idByManifestPath;
    private readonly Dictionary<string, FileSystemWatcher> _watchers;
    private HashSet<string> _knownLibraries;
    private GameEntry[] _cachedEntries = Array.Empty<GameEntry>();
    private bool _initialized;
    private IReadOnlyDictionary<uint, SteamAppDefinition> _accountApps = new Dictionary<uint, SteamAppDefinition>();
    private string? _accountId;

    public SteamAppManifestCache(
        ISteamLibraryLocator libraryLocator,
        ISteamClientAdapter clientAdapter,
        ISteamVdfFallback fallback,
        ValveTextVdfParser parser)
        : this(libraryLocator, clientAdapter, fallback, parser, new PlatformPathComparisonStrategy(new RuntimePlatformProvider()))
    {
    }

    public SteamAppManifestCache(
        ISteamLibraryLocator libraryLocator,
        ISteamClientAdapter clientAdapter,
        ISteamVdfFallback fallback,
        ValveTextVdfParser parser,
        IPathComparisonStrategy pathComparison)
    {
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
        _clientAdapter = clientAdapter ?? throw new ArgumentNullException(nameof(clientAdapter));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _pathComparison = pathComparison ?? throw new ArgumentNullException(nameof(pathComparison));
        _idByManifestPath = new Dictionary<string, GameIdentifier>(_pathComparison.Comparer);
        _watchers = new Dictionary<string, FileSystemWatcher>(_pathComparison.Comparer);
        _knownLibraries = new HashSet<string>(_pathComparison.Comparer);
    }

    public IReadOnlyCollection<GameEntry> GetInstalledGames()
    {
        lock (_syncRoot)
        {
            EnsureInitializedNoLock();
            return _cachedEntries;
        }
    }

    public void Refresh()
    {
        lock (_syncRoot)
        {
            var libraries = GetNormalizedLibraries();
            RefreshFromLibrariesNoLock(libraries);
            _initialized = true;
        }
    }

    private void EnsureInitializedNoLock()
    {
        if (!_initialized)
        {
            var libraries = GetNormalizedLibraries();
            RefreshFromLibrariesNoLock(libraries);
            _initialized = true;
            return;
        }

        EnsureLibrariesUpToDateNoLock();
    }

    private void EnsureLibrariesUpToDateNoLock()
    {
        var libraries = GetNormalizedLibraries();
        if (!_knownLibraries.SetEquals(libraries) || _accountId != _fallback.GetCurrentUserSteamId())
        {
            RefreshFromLibrariesNoLock(libraries);
        }
    }

    private HashSet<string> GetNormalizedLibraries()
    {
        var libraries = new HashSet<string>(_pathComparison.Comparer);
        foreach (var path in _libraryLocator.GetLibraryFolders())
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            libraries.Add(path.Trim());
        }

        return libraries;
    }

    private void RefreshFromLibrariesNoLock(HashSet<string> libraries)
    {
        _knownLibraries = libraries;

        var manifestDirectories = libraries
            .Select(library => Path.Combine(library, "steamapps"))
            .ToArray();

        UpdateWatchersNoLock(manifestDirectories);

        _accountApps = _fallback.GetKnownApps();
        _accountId = _fallback.GetCurrentUserSteamId();
        var installedSet = GetInstalledAppIds();
        var seenPaths = new HashSet<string>(_pathComparison.Comparer);

        foreach (var directory in manifestDirectories)
        {
            var manifestPaths = EnumerateManifestFiles(directory);
            if (manifestPaths is null)
            {
                // A temporarily unavailable drive is not evidence that its games were removed.
                foreach (var tracked in _idByManifestPath.Where(pair => _pathComparison.Equals(Path.GetDirectoryName(pair.Key)!, directory)))
                {
                    seenPaths.Add(tracked.Key);
                    MarkInstallationUnknownNoLock(tracked.Value, installedSet);
                }
                continue;
            }

            foreach (var manifestPath in manifestPaths)
            {
                seenPaths.Add(manifestPath);
                UpdateEntryFromManifestNoLock(manifestPath, installedSet, removeOnFailure: false);
            }
        }

        foreach (var existingPath in _idByManifestPath.Keys.ToList())
        {
            if (!seenPaths.Contains(existingPath))
            {
                RemoveEntryByPathNoLock(existingPath);
            }
        }

        UpdateCachedEntriesNoLock();
    }

    private void UpdateEntryFromManifestNoLock(string manifestPath, HashSet<uint> installedSet, bool removeOnFailure = true)
    {
        if (TryLoadManifest(manifestPath, installedSet, out var entry))
        {
            var id = entry.Id;
            _entries[id] = entry;
            TrackManifestPathNoLock(id, manifestPath);
        }
        else if (removeOnFailure)
        {
            RemoveEntryByPathNoLock(manifestPath);
        }
        else if (_idByManifestPath.TryGetValue(manifestPath, out var existingId))
        {
            MarkInstallationUnknownNoLock(existingId, installedSet);
        }
    }

    private void MarkInstallationUnknownNoLock(GameIdentifier id, HashSet<uint> installedSet)
    {
        if (!_entries.TryGetValue(id, out var entry)) return;
        var ownership = entry.SteamAppId is uint appId && _accountApps.TryGetValue(appId, out var definition)
            ? definition.OwnershipType : OwnershipType.Unknown;
        _entries[id] = entry with
        {
            InstallState = entry.SteamAppId is uint installedAppId && installedSet.Contains(installedAppId)
                ? InstallState.Installed : InstallState.Unknown,
            OwnershipType = ownership
        };
    }

    private static IEnumerable<string>? EnumerateManifestFiles(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsManifestPath)
                .ToArray();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private HashSet<uint> GetInstalledAppIds()
    {
        try
        {
            return _clientAdapter.GetInstalledAppIds().ToHashSet();
        }
        catch
        {
            return new HashSet<uint>();
        }
    }

    private bool TryLoadManifest(string manifestPath, HashSet<uint> installedSet, out GameEntry entry)
    {
        entry = default!;
        try
        {
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var root = _parser.Parse(stream);
            var appState = root.FindPath("AppState");
            if (appState is null)
            {
                return false;
            }

            if (!TryParseUInt(appState, "appid", out var appId) || appId == 0 ||
                !TryGetAppIdFromPath(manifestPath, out var fileAppId) || fileAppId != appId)
            {
                return false;
            }

            var title = GetString(appState, "name") ??
                        appState.FindPath("UserConfig", "name")?.Value ??
                        $"App {appId}";

            var sizeOnDisk = TryParseLong(appState, "SizeOnDisk");
            // LastOwner is installation history, not evidence of the current account's license.
            var ownershipType = _accountApps.TryGetValue(appId, out var accountApp)
                ? accountApp.OwnershipType
                : OwnershipType.Unknown;
            var stateFlags = TryParseLong(appState, "StateFlags");
            if (stateFlags < 0) stateFlags = null;
            var installState = installedSet.Contains(appId) || (stateFlags.HasValue && (stateFlags.Value & 4) != 0)
                ? InstallState.Installed
                : stateFlags.HasValue ? InstallState.Available : InstallState.Unknown;

            var lastPlayed = ParseLastPlayed(appState.FindPath("UserConfig", "LastPlayed"));

            entry = new GameEntry
            {
                Id = GameIdentifier.ForSteam(appId),
                Title = title,
                OwnershipType = ownershipType,
                InstallState = installState,
                SizeOnDisk = sizeOnDisk,
                LastPlayed = lastPlayed
            };

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private void TrackManifestPathNoLock(GameIdentifier id, string manifestPath)
    {
        foreach (var existingPath in _idByManifestPath
                     .Where(pair => pair.Value.Equals(id) &&
                                    !string.Equals(pair.Key, manifestPath, StringComparison.Ordinal))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _idByManifestPath.Remove(existingPath);
        }

        _manifestPathById[id] = manifestPath;
        _idByManifestPath[manifestPath] = id;
    }

    private static string? GetString(ValveKeyValueNode parent, string childName)
        => parent.TryGetChild(childName, out var child) ? child.Value : null;

    private static bool TryParseUInt(ValveKeyValueNode parent, string childName, out uint value)
    {
        value = 0;
        if (!parent.TryGetChild(childName, out var node) || node.Value is null)
        {
            return false;
        }

        return uint.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static long? TryParseLong(ValveKeyValueNode parent, string childName)
    {
        if (!parent.TryGetChild(childName, out var node) || node.Value is null)
        {
            return null;
        }

        if (long.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    private static DateTimeOffset? ParseLastPlayed(ValveKeyValueNode? node)
    {
        if (node?.Value is null)
        {
            return null;
        }

        if (!long.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private void RemoveEntryByPathNoLock(string manifestPath)
    {
        if (TryGetExactTrackedManifestPathNoLock(manifestPath, out var trackedPath, out var id))
        {
            _idByManifestPath.Remove(trackedPath);
        }
        else
        {
            if (TryGetAppIdFromPath(manifestPath, out var extracted))
            {
                id = GameIdentifier.ForSteam(extracted);
            }
            else
            {
                return;
            }
        }

        if (_manifestPathById.TryGetValue(id, out var storedPath) &&
            string.Equals(storedPath, manifestPath, StringComparison.Ordinal))
        {
            _manifestPathById.Remove(id);
            _entries.Remove(id);
        }
        else if (!_manifestPathById.ContainsKey(id))
        {
            _entries.Remove(id);
        }

        UpdateCachedEntriesNoLock();
    }

    private bool TryGetExactTrackedManifestPathNoLock(string manifestPath, out string trackedPath, out GameIdentifier id)
    {
        foreach (var pair in _idByManifestPath)
        {
            if (string.Equals(pair.Key, manifestPath, StringComparison.Ordinal))
            {
                trackedPath = pair.Key;
                id = pair.Value;
                return true;
            }
        }

        trackedPath = string.Empty;
        id = default!;
        return false;
    }

    private static bool TryGetAppIdFromPath(string manifestPath, out uint appId)
    {
        var fileName = Path.GetFileNameWithoutExtension(manifestPath);
        const string prefix = "appmanifest_";
        if (fileName is null || !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            appId = 0;
            return false;
        }

        var span = fileName.AsSpan(prefix.Length);
        return uint.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out appId);
    }

    private void UpdateCachedEntriesNoLock()
    {
        _cachedEntries = _entries.Values
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, GameIdentifier.Comparer)
            .ToArray();
    }

    private void UpdateWatchersNoLock(IEnumerable<string> manifestDirectories)
    {
        var desired = new HashSet<string>(manifestDirectories, _pathComparison.Comparer);

        foreach (var existing in _watchers.Keys.ToList())
        {
            if (!desired.Contains(existing))
            {
                _watchers[existing].Dispose();
                _watchers.Remove(existing);
            }
        }

        foreach (var directory in desired)
        {
            if (_watchers.ContainsKey(directory))
            {
                continue;
            }

            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(directory, "*")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                watcher.Changed += OnManifestChanged;
                watcher.Created += OnManifestChanged;
                watcher.Renamed += OnManifestRenamed;
                watcher.Deleted += OnManifestDeleted;
                watcher.Error += OnWatcherError;

                _watchers[directory] = watcher;
            }
            catch (IOException)
            {
                // ignore watcher setup errors
            }
            catch (UnauthorizedAccessException)
            {
                // ignore watcher setup errors
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        lock (_syncRoot)
        {
            _initialized = false;
        }
    }

    private void OnManifestChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsManifestPath(e.FullPath))
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!_initialized)
            {
                return;
            }

            var installedSet = GetInstalledAppIds();
            UpdateEntryFromManifestNoLock(e.FullPath, installedSet, removeOnFailure: false);
            UpdateCachedEntriesNoLock();
        }
    }

    private void OnManifestRenamed(object sender, RenamedEventArgs e)
    {
        var oldPathWasManifest = IsManifestPath(e.OldFullPath);
        var newPathIsManifest = IsManifestPath(e.FullPath);

        lock (_syncRoot)
        {
            if (!_initialized)
            {
                return;
            }

            if (oldPathWasManifest && (!newPathIsManifest || !_pathComparison.Equals(e.OldFullPath, e.FullPath)))
            {
                RemoveEntryByPathNoLock(e.OldFullPath);
            }

            if (newPathIsManifest)
            {
                var installedSet = GetInstalledAppIds();
                UpdateEntryFromManifestNoLock(e.FullPath, installedSet);
                UpdateCachedEntriesNoLock();
            }
        }
    }

    private void OnManifestDeleted(object sender, FileSystemEventArgs e)
    {
        if (!IsManifestPath(e.FullPath))
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!_initialized)
            {
                return;
            }

            RemoveEntryByPathNoLock(e.FullPath);
        }
    }

    private static bool IsManifestPath(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        return fileName is not null && fileName.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".acf", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            _entries.Clear();
            _manifestPathById.Clear();
            _idByManifestPath.Clear();
            _cachedEntries = Array.Empty<GameEntry>();
            _initialized = false;
        }
    }
}
