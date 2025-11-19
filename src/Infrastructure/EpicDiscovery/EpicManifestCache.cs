using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Domain;
using Microsoft.Extensions.Logging;
using SteamClientAdapter;

namespace EpicDiscovery;

public sealed class EpicManifestCache : IDisposable
{
    private readonly IEpicLauncherLocator launcherLocator;
    private readonly IFileAccessor fileAccessor;
    private readonly ILogger<EpicManifestCache>? logger;
    private readonly object syncRoot = new();
    private readonly Dictionary<GameIdentifier, GameEntry> entries = new();
    private readonly Dictionary<string, GameIdentifier> idByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GameIdentifier, string> pathById = new();
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<GameIdentifier> launcherInstalledEntries = new();
    private HashSet<string> knownDirectories = new(StringComparer.OrdinalIgnoreCase);
    private GameEntry[] cachedEntries = Array.Empty<GameEntry>();
    private bool disposed;
    private bool initialized;

    public EpicManifestCache(
        IEpicLauncherLocator launcherLocator,
        IFileAccessor fileAccessor,
        ILogger<EpicManifestCache>? logger = null)
    {
        this.launcherLocator = launcherLocator ?? throw new ArgumentNullException(nameof(launcherLocator));
        this.fileAccessor = fileAccessor ?? throw new ArgumentNullException(nameof(fileAccessor));
        this.logger = logger;
    }

    public IReadOnlyCollection<GameEntry> GetInstalledGames()
    {
        lock (syncRoot)
        {
            EnsureInitializedNoLock();
            return cachedEntries;
        }
    }

    public void Refresh()
    {
        lock (syncRoot)
        {
            var directories = GetDirectories();
            RefreshFromDirectoriesNoLock(directories);
        }
    }

    private void EnsureInitializedNoLock()
    {
        if (!initialized)
        {
            var directories = GetDirectories();
            RefreshFromDirectoriesNoLock(directories);
            initialized = true;
            return;
        }

        EnsureDirectoriesUpToDateNoLock();
    }

    private void EnsureDirectoriesUpToDateNoLock()
    {
        var directories = GetDirectories();
        if (!knownDirectories.SetEquals(directories))
        {
            RefreshFromDirectoriesNoLock(directories);
        }
    }

    private HashSet<string> GetDirectories()
    {
        var directories = launcherLocator.GetManifestDirectories();
        return directories.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : directories.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshFromDirectoriesNoLock(HashSet<string> directories)
    {
        knownDirectories = directories;
        UpdateWatchersNoLock(directories);

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(directory, "*.item", SearchOption.TopDirectoryOnly))
            {
                seenPaths.Add(manifestPath);
                UpdateEntryFromManifestNoLock(manifestPath);
            }
        }

        RefreshFromLauncherInstalledDatNoLock();

        foreach (var existingPath in idByPath.Keys.ToList())
        {
            if (!seenPaths.Contains(existingPath) || !File.Exists(existingPath))
            {
                RemoveEntryByPathNoLock(existingPath);
            }
        }

        UpdateCachedEntriesNoLock();
    }

    private void RefreshFromLauncherInstalledDatNoLock()
    {
        var datPath = launcherLocator.GetLauncherInstalledDatPath();
        if (string.IsNullOrWhiteSpace(datPath) || !File.Exists(datPath))
        {
            return;
        }

        var seenLauncherInstalledIds = new HashSet<GameIdentifier>();

        try
        {
            using var stream = fileAccessor.OpenRead(datPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("InstallationList", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (TryLoadFromLauncherInstalledItem(item, out var entry))
                    {
                        // Only add if not already present (manifests take precedence)
                        if (!pathById.ContainsKey(entry.Id))
                        {
                            entries[entry.Id] = entry;
                            seenLauncherInstalledIds.Add(entry.Id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse LauncherInstalled.dat at {Path}", datPath);
        }

        foreach (var staleId in launcherInstalledEntries.Where(id => !seenLauncherInstalledIds.Contains(id)).ToList())
        {
            if (!pathById.ContainsKey(staleId))
            {
                entries.Remove(staleId);
            }
        }

        launcherInstalledEntries = seenLauncherInstalledIds;
    }

    private bool TryLoadFromLauncherInstalledItem(JsonElement item, out GameEntry entry)
    {
        entry = default!;
        var installLocation = TryGetString(item, "InstallLocation");
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return false;
        }

        var appName = TryGetString(item, "AppName") ?? Path.GetFileName(installLocation);
        var title = appName; // LauncherInstalled.dat doesn't always have a nice display name, fallback to AppName
        var version = TryGetString(item, "AppVersion");

        // Try to find a better name from .egstore if possible, or just use what we have
        // For now, we'll use AppName as the ID base

        entry = new GameEntry
        {
            Id = EpicIdentifierFactory.Create(null, null, appName),
            Title = title,
            OwnershipType = OwnershipType.Owned,
            InstallState = InstallState.Installed,
            SizeOnDisk = 0, // Not available in this file usually
            LastPlayed = null, // Not available
            Tags = Array.Empty<string>(),
        };

        return true;
    }

    private void UpdateEntryFromManifestNoLock(string manifestPath)
    {
        if (TryLoadManifest(manifestPath, out var entry))
        {
            var id = entry.Id;
            entries[id] = entry;
            idByPath[manifestPath] = id;
            pathById[id] = manifestPath;
        }
        else
        {
            RemoveEntryByPathNoLock(manifestPath);
        }
    }

    private bool TryLoadManifest(string manifestPath, out GameEntry entry)
    {
        entry = default!;
        try
        {
            if (!fileAccessor.FileExists(manifestPath))
            {
                return false;
            }

            using var stream = fileAccessor.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            var catalogNamespace = TryGetString(root, "CatalogNamespace");
            var catalogItemId = TryGetString(root, "CatalogItemId") ?? TryGetString(root, "MainGameCatalogItemId");
            var appName = TryGetString(root, "AppName") ?? Path.GetFileNameWithoutExtension(manifestPath);
            var title = TryGetString(root, "DisplayName") ??
                        TryGetString(root, "AppTitle") ??
                        TryGetString(root, "Title") ??
                        appName ??
                        Path.GetFileNameWithoutExtension(manifestPath);

            var installState = GetInstallState(root);
            var sizeOnDisk = TryGetLong(root, "InstallSize") ?? TryGetLong(root, "DiskSize") ?? TryGetLong(root, "Size");
            var lastPlayed = TryGetDateTime(root, "LastPlayedDate") ??
                             TryGetDateTime(root, "LastUpdated") ??
                             TryGetDateTime(root, "LastModified") ??
                             TryGetDateTime(root, "LastRun") ??
                             TryGetDateTime(root, "LastLaunchTime");

            var tags = ExtractTags(root);

            entry = new GameEntry
            {
                Id = EpicIdentifierFactory.Create(catalogItemId, catalogNamespace, appName),
                Title = title ?? "Unknown Epic Game",
                OwnershipType = OwnershipType.Owned,
                InstallState = installState,
                SizeOnDisk = sizeOnDisk,
                LastPlayed = lastPlayed,
                Tags = tags,
            };

            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse Epic manifest {Manifest}", manifestPath);
            return false;
        }
    }

    private static InstallState GetInstallState(JsonElement root)
    {
        if (TryGetBoolean(root, "bIsInstalled", out var isInstalled))
        {
            return isInstalled ? InstallState.Installed : InstallState.Available;
        }

        var status = TryGetString(root, "InstallationStatus") ?? TryGetString(root, "State");
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status.Equals("Installed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                return InstallState.Installed;
            }

            if (status.Equals("Paused", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                return InstallState.Available;
            }
        }

        return InstallState.Installed;
    }

    private static string[] ExtractTags(JsonElement root)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in categories.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    tags.Add(value!);
                }
            }
        }

        if (root.TryGetProperty("Categories", out var nestedCategories) && nestedCategories.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in nestedCategories.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        tags.Add(value!);
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("Path", out var pathProp))
                {
                    var value = pathProp.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        tags.Add(value!);
                    }
                }
            }
        }

        return tags.ToArray();
    }

    private static bool TryGetBoolean(JsonElement root, string property, out bool value)
    {
        value = default;
        if (root.TryGetProperty(property, out var element))
        {
            if (element.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            if (element.ValueKind == JsonValueKind.False)
            {
                value = false;
                return true;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                if (bool.TryParse(text, out var parsed))
                {
                    value = parsed;
                    return true;
                }
            }
        }

        return false;
    }

    private static string? TryGetString(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private static long? TryGetLong(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var element))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value))
            {
                return value;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                if (long.TryParse(text, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? TryGetDateTime(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var element))
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private void RemoveEntryByPathNoLock(string path)
    {
        if (!idByPath.TryGetValue(path, out var id))
        {
            return;
        }

        idByPath.Remove(path);
        pathById.Remove(id);
        entries.Remove(id);
    }

    private void UpdateCachedEntriesNoLock()
    {
        cachedEntries = entries.Values
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id.StoreSpecificId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void UpdateWatchersNoLock(IEnumerable<string> directories)
    {
        foreach (var directory in watchers.Keys.ToList())
        {
            if (!directories.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                watchers[directory].Dispose();
                watchers.Remove(directory);
            }
        }

        foreach (var directory in directories)
        {
            if (!watchers.ContainsKey(directory) && Directory.Exists(directory))
            {
                var watcher = new FileSystemWatcher(directory, "*.item")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                };

                watcher.Created += OnManifestChanged;
                watcher.Changed += OnManifestChanged;
                watcher.Deleted += OnManifestChanged;
                watcher.Renamed += OnManifestRenamed;

                watchers[directory] = watcher;
            }
        }
    }

    private void OnManifestChanged(object sender, FileSystemEventArgs e)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                RemoveEntryByPathNoLock(e.FullPath);
                UpdateCachedEntriesNoLock();
                return;
            }

            UpdateEntryFromManifestNoLock(e.FullPath);
            UpdateCachedEntriesNoLock();
        }
    }

    private void OnManifestRenamed(object sender, RenamedEventArgs e)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            RemoveEntryByPathNoLock(e.OldFullPath);
            UpdateEntryFromManifestNoLock(e.FullPath);
            UpdateCachedEntriesNoLock();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            foreach (var watcher in watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= OnManifestChanged;
                watcher.Changed -= OnManifestChanged;
                watcher.Deleted -= OnManifestChanged;
                watcher.Renamed -= OnManifestRenamed;
                watcher.Dispose();
            }

            watchers.Clear();
        }
    }
}
