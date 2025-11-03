using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SteamClientAdapter;

namespace EpicDiscovery;

public sealed class EpicCatalogCache : IDisposable
{
    private static readonly string[] JsonExtensions = [".json", ".js", ".catalog"];
    private static readonly string[] SqliteExtensions = [".sqlite", ".db", ".cache"];

    private readonly IEpicLauncherLocator launcherLocator;
    private readonly IFileAccessor fileAccessor;
    private readonly ILogger<EpicCatalogCache>? logger;
    private readonly object syncRoot = new();
    private readonly Dictionary<GameIdentifier, EpicCatalogItem> entries = new();
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> knownDirectories = new(StringComparer.OrdinalIgnoreCase);
    private EpicCatalogItem[] cachedEntries = Array.Empty<EpicCatalogItem>();
    private bool initialized;
    private bool disposed;

    public EpicCatalogCache(
        IEpicLauncherLocator launcherLocator,
        IFileAccessor fileAccessor,
        ILogger<EpicCatalogCache>? logger = null)
    {
        this.launcherLocator = launcherLocator ?? throw new ArgumentNullException(nameof(launcherLocator));
        this.fileAccessor = fileAccessor ?? throw new ArgumentNullException(nameof(fileAccessor));
        this.logger = logger;
    }

    public IReadOnlyCollection<EpicCatalogItem> GetCatalogEntries()
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
        var directories = launcherLocator.GetCatalogDirectories();
        return directories.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : directories.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshFromDirectoriesNoLock(HashSet<string> directories)
    {
        knownDirectories = directories;
        UpdateWatchersNoLock(directories);

        var newEntries = new Dictionary<GameIdentifier, EpicCatalogItem>();

        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray();
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Skipping Epic catalog directory {Directory}", directory);
                continue;
            }

            foreach (var filePath in files)
            {
                try
                {
                    foreach (var item in LoadCatalogItems(filePath))
                    {
                        newEntries[item.Id] = item;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to parse Epic catalog cache {File}", filePath);
                }
            }
        }

        entries.Clear();
        foreach (var kvp in newEntries)
        {
            entries[kvp.Key] = kvp.Value;
        }

        cachedEntries = entries.Values
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id.StoreSpecificId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IEnumerable<EpicCatalogItem> LoadCatalogItems(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (JsonExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return LoadFromJson(filePath);
        }

        if (SqliteExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return LoadFromSqlite(filePath);
        }

        return Array.Empty<EpicCatalogItem>();
    }

    private IEnumerable<EpicCatalogItem> LoadFromJson(string path)
    {
        if (!fileAccessor.FileExists(path))
        {
            return Array.Empty<EpicCatalogItem>();
        }

        using var stream = fileAccessor.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var results = new List<EpicCatalogItem>();
        foreach (var element in EnumerateCatalogElements(root))
        {
            var item = ParseCatalogElement(element);
            if (item is not null)
            {
                results.Add(item);
            }
        }

        return results;
    }

    private IEnumerable<EpicCatalogItem> LoadFromSqlite(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        };

        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        var tableNames = GetTableNames(connection);
        var results = new List<EpicCatalogItem>();

        foreach (var table in tableNames)
        {
            if (!IsCatalogTable(table))
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\"";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var item = ParseCatalogRow(reader);
                if (item is not null)
                {
                    results.Add(item);
                }
            }
        }

        return results;
    }

    private static HashSet<string> GetTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";

        using var reader = command.ExecuteReader();
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    private static bool IsCatalogTable(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return false;
        }

        return tableName.Contains("catalog", StringComparison.OrdinalIgnoreCase) ||
               tableName.Contains("item", StringComparison.OrdinalIgnoreCase) ||
               tableName.Contains("offer", StringComparison.OrdinalIgnoreCase);
    }

    private EpicCatalogItem? ParseCatalogRow(SqliteDataReader reader)
    {
        var catalogItemId = GetReaderString(reader, ["CatalogItemId", "Id", "ItemId", "OfferId"]);
        var catalogNamespace = GetReaderString(reader, ["CatalogNamespace", "Namespace", "NamespaceId"]);
        var appName = GetReaderString(reader, ["AppName", "ApplicationId", "TechnicalName"]);
        var title = GetReaderString(reader, ["Title", "DisplayName", "Name", "AppTitle"]) ??
                    appName ?? catalogItemId;

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(catalogItemId))
        {
            return null;
        }

        var size = GetReaderLong(reader, ["InstallSize", "DiskSize", "SizeOnDisk", "Size"]);
        var lastModified = GetReaderDate(reader, ["LastModified", "LastModifiedDate", "Updated", "UpdatedAt"]);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTagsFromReader(reader, tags, "Categories");
        CollectTagsFromReader(reader, tags, "CategoryPath");
        CollectTagsFromReader(reader, tags, "Tags");
        CollectTagsFromReader(reader, tags, "Genres");

        var id = EpicIdentifierFactory.Create(catalogItemId, catalogNamespace, appName);

        return new EpicCatalogItem
        {
            Id = id,
            CatalogItemId = catalogItemId,
            CatalogNamespace = catalogNamespace,
            AppName = appName,
            Title = title ?? "Unknown Epic Game",
            Tags = tags.ToArray(),
            SizeOnDisk = size,
            LastModified = lastModified,
        };
    }

    private static string? GetReaderString(SqliteDataReader reader, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var ordinal = TryGetOrdinal(reader, candidate);
            if (ordinal >= 0 && !reader.IsDBNull(ordinal))
            {
                return reader.GetValue(ordinal)?.ToString();
            }
        }

        return null;
    }

    private static long? GetReaderLong(SqliteDataReader reader, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var ordinal = TryGetOrdinal(reader, candidate);
            if (ordinal >= 0 && !reader.IsDBNull(ordinal))
            {
                var value = reader.GetValue(ordinal);
                if (value is long longValue)
                {
                    return longValue;
                }

                if (value is int intValue)
                {
                    return intValue;
                }

                if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? GetReaderDate(SqliteDataReader reader, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var ordinal = TryGetOrdinal(reader, candidate);
            if (ordinal >= 0 && !reader.IsDBNull(ordinal))
            {
                var text = reader.GetValue(ordinal)?.ToString();
                if (!string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static void CollectTagsFromReader(SqliteDataReader reader, HashSet<string> tags, string column)
    {
        var ordinal = TryGetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return;
        }

        var value = reader.GetValue(ordinal)?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var tag in ParseTags(value))
        {
            tags.Add(tag);
        }
    }

    private static int TryGetOrdinal(SqliteDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private EpicCatalogItem? ParseCatalogElement(JsonElement element)
    {
        var catalogItemId = TryGetString(element, "CatalogItemId", "catalogItemId", "id", "offerId");
        var catalogNamespace = TryGetString(element, "CatalogNamespace", "catalogNamespace", "namespace", "namespaceId");
        var appName = TryGetString(element, "AppName", "appName", "technicalName");
        var title = TryGetString(element, "DisplayName", "displayName", "title", "name", "appTitle") ??
                    appName ?? catalogItemId;

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(catalogItemId))
        {
            return null;
        }

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTagsFromElement(element, tags, "categories");
        CollectTagsFromElement(element, tags, "genres");
        CollectTagsFromElement(element, tags, "tags");
        CollectTagsFromElement(element, tags, "keywords");

        var size = TryGetLong(element, "InstallSize", "installSize", "diskSize", "size");
        var lastModified = TryGetDate(element, "LastModified", "lastModified", "updated", "updatedAt");

        var id = EpicIdentifierFactory.Create(catalogItemId, catalogNamespace, appName);

        return new EpicCatalogItem
        {
            Id = id,
            CatalogItemId = catalogItemId,
            CatalogNamespace = catalogNamespace,
            AppName = appName,
            Title = title ?? "Unknown Epic Game",
            Tags = tags.ToArray(),
            SizeOnDisk = size,
            LastModified = lastModified,
        };
    }

    private static IEnumerable<JsonElement> EnumerateCatalogElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    yield return element;
                }
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in new[] { "elements", "items", "data", "CatalogItems", "catalogItems" })
            {
                if (root.TryGetProperty(property, out var collection) && collection.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in collection.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            yield return element;
                        }
                    }
                }
            }

            yield return root;
        }
    }

    private static void CollectTagsFromElement(JsonElement element, HashSet<string> tags, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        tags.Add(text!);
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("path", out var path))
                {
                    var text = path.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        tags.Add(text!);
                    }
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            foreach (var tag in ParseTags(value.GetString()))
            {
                tags.Add(tag);
            }
        }
    }

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                return property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString(),
                    JsonValueKind.Number => property.GetRawText(),
                    _ => null
                };
            }
        }

        return null;
    }

    private static long? TryGetLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
                {
                    return value;
                }

                if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? TryGetDate(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString();
                if (!string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ParseTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        value = value.Trim();
        if (value.StartsWith("[", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(value);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var results = new List<string>();
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String)
                        {
                            var text = element.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                results.Add(text!);
                            }
                        }
                    }

                    return results;
                }
            }
            catch
            {
                // ignore malformed JSON tag arrays
            }
        }

        return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToArray();
    }

    private void UpdateWatchersNoLock(IEnumerable<string> directories)
    {
        foreach (var directory in watchers.Keys.ToList())
        {
            if (!directories.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                watchers[directory].EnableRaisingEvents = false;
                watchers[directory].Changed -= OnCacheChanged;
                watchers[directory].Created -= OnCacheChanged;
                watchers[directory].Deleted -= OnCacheChanged;
                watchers[directory].Renamed -= OnCacheRenamed;
                watchers[directory].Dispose();
                watchers.Remove(directory);
            }
        }

        foreach (var directory in directories)
        {
            if (!watchers.ContainsKey(directory) && Directory.Exists(directory))
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                };

                watcher.Changed += OnCacheChanged;
                watcher.Created += OnCacheChanged;
                watcher.Deleted += OnCacheChanged;
                watcher.Renamed += OnCacheRenamed;

                watchers[directory] = watcher;
            }
        }
    }

    private void OnCacheChanged(object sender, FileSystemEventArgs e)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            RefreshFromDirectoriesNoLock(new HashSet<string>(knownDirectories, StringComparer.OrdinalIgnoreCase));
        }
    }

    private void OnCacheRenamed(object sender, RenamedEventArgs e)
    {
        OnCacheChanged(sender, e);
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
                watcher.Changed -= OnCacheChanged;
                watcher.Created -= OnCacheChanged;
                watcher.Deleted -= OnCacheChanged;
                watcher.Renamed -= OnCacheRenamed;
                watcher.Dispose();
            }

            watchers.Clear();
        }
    }
}
