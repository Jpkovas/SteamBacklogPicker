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
    private readonly object _syncRoot = new();
    private readonly Dictionary<uint, GameEntry> _entries = new();
    private readonly Dictionary<uint, string> _manifestPathByAppId = new();
    private readonly Dictionary<string, uint> _appIdByManifestPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _knownLibraries = new(StringComparer.OrdinalIgnoreCase);
    private GameEntry[] _cachedEntries = Array.Empty<GameEntry>();
    private bool _initialized;

    public SteamAppManifestCache(
        ISteamLibraryLocator libraryLocator,
        ISteamClientAdapter clientAdapter,
        ISteamVdfFallback fallback,
        ValveTextVdfParser parser)
    {
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
        _clientAdapter = clientAdapter ?? throw new ArgumentNullException(nameof(clientAdapter));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
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
        if (!_knownLibraries.SetEquals(libraries))
        {
            RefreshFromLibrariesNoLock(libraries);
        }
    }

    private HashSet<string> GetNormalizedLibraries()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        var installedSet = GetInstalledAppIds();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in manifestDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(directory, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                seenPaths.Add(manifestPath);
                UpdateEntryFromManifestNoLock(manifestPath, installedSet);
            }
        }

        foreach (var existingPath in _appIdByManifestPath.Keys.ToList())
        {
            if (!seenPaths.Contains(existingPath) || !File.Exists(existingPath))
            {
                RemoveEntryByPathNoLock(existingPath);
            }
        }

        UpdateCachedEntriesNoLock();
    }

    private void UpdateEntryFromManifestNoLock(string manifestPath, HashSet<uint> installedSet)
    {
        if (TryLoadManifest(manifestPath, installedSet, out var entry))
        {
            _entries[entry.AppId] = entry;
            _manifestPathByAppId[entry.AppId] = manifestPath;
            _appIdByManifestPath[manifestPath] = entry.AppId;
        }
        else
        {
            RemoveEntryByPathNoLock(manifestPath);
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

            var content = File.ReadAllText(manifestPath);
            var root = _parser.Parse(content);
            var appState = root.FindPath("AppState");
            if (appState is null)
            {
                return false;
            }

            if (!TryParseUInt(appState, "appid", out var appId))
            {
                return false;
            }

            var title = GetString(appState, "name") ??
                        appState.FindPath("UserConfig", "name")?.Value ??
                        $"App {appId}";

            var sizeOnDisk = TryParseLong(appState, "SizeOnDisk");
            var lastOwner = GetString(appState, "LastOwner");
            var isFamilyShared = IsFamilyShared(appId) || IsFamilySharedByOwner(lastOwner);

            var ownershipType = isFamilyShared ? OwnershipType.FamilyShared : OwnershipType.Owned;

            var installState = isFamilyShared ? InstallState.Shared : InstallState.Installed;
            if (!isFamilyShared && installedSet.Count > 0 && !installedSet.Contains(appId))
            {
                installState = InstallState.Available;
            }

            var lastPlayed = ParseLastPlayed(appState.FindPath("UserConfig", "LastPlayed"));

            entry = new GameEntry
            {
                AppId = appId,
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
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
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

    private bool IsFamilySharedByOwner(string? lastOwner)
    {
        if (string.IsNullOrWhiteSpace(lastOwner))
        {
            return false;
        }

        var currentOwner = _fallback.GetCurrentUserSteamId();
        if (string.IsNullOrWhiteSpace(currentOwner))
        {
            return false;
        }

        lastOwner = lastOwner.Trim();
        currentOwner = currentOwner.Trim();

        if (string.Equals(lastOwner, currentOwner, StringComparison.Ordinal))
        {
            return false;
        }

        if (TryNormalizeSteamId(lastOwner, out var lastOwnerId) &&
            TryNormalizeSteamId(currentOwner, out var currentOwnerId))
        {
            return lastOwnerId != currentOwnerId;
        }

        return !string.Equals(lastOwner, currentOwner, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsFamilyShared(uint appId)
    {
        try
        {
            return _clientAdapter.IsSubscribedFromFamilySharing(appId);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryNormalizeSteamId(string value, out ulong id)
    {
        if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        id = 0;
        return false;
    }

    private void RemoveEntryByPathNoLock(string manifestPath)
    {
        if (!_appIdByManifestPath.TryGetValue(manifestPath, out var appId))
        {
            if (TryGetAppIdFromPath(manifestPath, out var extracted))
            {
                appId = extracted;
            }
            else
            {
                return;
            }
        }

        _appIdByManifestPath.Remove(manifestPath);

        if (_manifestPathByAppId.TryGetValue(appId, out var storedPath) &&
            string.Equals(storedPath, manifestPath, StringComparison.OrdinalIgnoreCase))
        {
            _manifestPathByAppId.Remove(appId);
            _entries.Remove(appId);
        }
        else if (!_manifestPathByAppId.ContainsKey(appId))
        {
            _entries.Remove(appId);
        }

        UpdateCachedEntriesNoLock();
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
            .ThenBy(entry => entry.AppId)
            .ToArray();
    }

    private void UpdateWatchersNoLock(IEnumerable<string> manifestDirectories)
    {
        var desired = new HashSet<string>(manifestDirectories, StringComparer.OrdinalIgnoreCase);

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
                var watcher = new FileSystemWatcher(directory, "appmanifest_*.acf")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                watcher.Changed += OnManifestChanged;
                watcher.Created += OnManifestChanged;
                watcher.Renamed += OnManifestRenamed;
                watcher.Deleted += OnManifestDeleted;

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
            UpdateEntryFromManifestNoLock(e.FullPath, installedSet);
            UpdateCachedEntriesNoLock();
        }
    }

    private void OnManifestRenamed(object sender, RenamedEventArgs e)
    {
        lock (_syncRoot)
        {
            if (!_initialized)
            {
                return;
            }

            if (IsManifestPath(e.OldFullPath))
            {
                RemoveEntryByPathNoLock(e.OldFullPath);
            }

            if (IsManifestPath(e.FullPath))
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
            _manifestPathByAppId.Clear();
            _appIdByManifestPath.Clear();
            _cachedEntries = Array.Empty<GameEntry>();
            _initialized = false;
        }
    }
}
