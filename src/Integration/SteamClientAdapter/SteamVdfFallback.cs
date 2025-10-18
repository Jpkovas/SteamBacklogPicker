using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Domain;
using ValveFormatParser;
using ValveKeyValue;
using ZstdSharp;

namespace SteamClientAdapter;

public interface ISteamVdfFallback
{
    IReadOnlyCollection<uint> GetInstalledAppIds();

    bool IsSubscribedFromFamilySharing(uint appId);

    IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps();

    string? GetCurrentUserSteamId();

    IReadOnlyList<SteamCollectionDefinition> GetCollections();

    void Invalidate();
}

public sealed class SteamVdfFallback : ISteamVdfFallback
{
    private readonly string _steamDirectory;
    private readonly IFileAccessor _files;
    private readonly ValveTextVdfParser _textParser;
    private readonly ValveBinaryVdfParser _binaryParser;
    private readonly ConcurrentDictionary<uint, bool> _familySharingCache = new();
    private readonly Dictionary<uint, string> _appNames = new();
    private readonly Dictionary<uint, string> _appTypes = new();
    private readonly Dictionary<uint, IReadOnlyList<int>> _appCategories = new();
    private readonly Dictionary<uint, SteamDeckCompatibility> _appDeckCompatibility = new();
    private readonly object _cacheLock = new();
    private IReadOnlyDictionary<uint, SteamAppDefinition>? _cachedAppDefinitions;
    private CacheSnapshot? _cachedAppSnapshot;
    private IReadOnlyDictionary<uint, SteamAppDefinition>? _collectionsSource;
    private IReadOnlyList<SteamCollectionDefinition>? _collectionDefinitions;
    private bool _appInfoLoaded;
    private string? _currentSteamId;

    public SteamVdfFallback(
        string steamDirectory,
        IFileAccessor files,
        ValveTextVdfParser textParser,
        ValveBinaryVdfParser binaryParser)
    {
        _steamDirectory = steamDirectory ?? throw new ArgumentNullException(nameof(steamDirectory));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _textParser = textParser ?? throw new ArgumentNullException(nameof(textParser));
        _binaryParser = binaryParser ?? throw new ArgumentNullException(nameof(binaryParser));
    }

    public IReadOnlyCollection<uint> GetInstalledAppIds()
    {
        return GetKnownApps()
            .Values
            .Where(app => app.IsInstalled)
            .Select(app => app.AppId)
            .ToArray();
    }

    public IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps()
    {
        lock (_cacheLock)
        {
            if (_cachedAppDefinitions is not null &&
                _cachedAppSnapshot is not null &&
                CacheIsFresh(_cachedAppSnapshot))
            {
                return _cachedAppDefinitions;
            }
        }

        var result = LoadAppDefinitions();

        lock (_cacheLock)
        {
            if (_cachedAppDefinitions is not null &&
                _cachedAppSnapshot is not null &&
                CacheIsFresh(_cachedAppSnapshot))
            {
                return _cachedAppDefinitions;
            }

            _cachedAppDefinitions = result.Definitions;
            _cachedAppSnapshot = result.Snapshot;
            _currentSteamId = result.SteamId;
            _collectionDefinitions = null;
            _collectionsSource = null;

            return _cachedAppDefinitions;
        }
    }

    public string? GetCurrentUserSteamId()
    {
        lock (_cacheLock)
        {
            if (_currentSteamId is not null)
            {
                return _currentSteamId;
            }
        }

        // Ensure metadata is loaded so that we attempt to resolve the user id.
        _ = GetKnownApps();

        lock (_cacheLock)
        {
            return _currentSteamId;
        }
    }

    public IReadOnlyList<SteamCollectionDefinition> GetCollections()
    {
        while (true)
        {
            IReadOnlyDictionary<uint, SteamAppDefinition>? definitionsSnapshot;
            IReadOnlyList<SteamCollectionDefinition>? cachedCollections;

            lock (_cacheLock)
            {
                definitionsSnapshot = _cachedAppDefinitions;
                cachedCollections = _collectionDefinitions;

                if (definitionsSnapshot is not null &&
                    cachedCollections is not null &&
                    ReferenceEquals(_collectionsSource, definitionsSnapshot))
                {
                    return cachedCollections;
                }
            }

            _ = GetKnownApps();

            string? steamId;
            lock (_cacheLock)
            {
                definitionsSnapshot = _cachedAppDefinitions;
                cachedCollections = _collectionDefinitions;

                if (definitionsSnapshot is not null &&
                    cachedCollections is not null &&
                    ReferenceEquals(_collectionsSource, definitionsSnapshot))
                {
                    return cachedCollections;
                }

                steamId = _currentSteamId;
            }

            IReadOnlyList<SteamCollectionDefinition> loadedCollections;
            if (string.IsNullOrWhiteSpace(steamId))
            {
                loadedCollections = Array.Empty<SteamCollectionDefinition>();
            }
            else
            {
                loadedCollections = LoadCollectionsFromCloudStorage(steamId);
            }

            lock (_cacheLock)
            {
                if (!ReferenceEquals(definitionsSnapshot, _cachedAppDefinitions))
                {
                    continue;
                }

                _collectionDefinitions = loadedCollections;
                _collectionsSource = definitionsSnapshot;
                return loadedCollections;
            }
        }
    }

    public void Invalidate()
    {
        lock (_cacheLock)
        {
            _cachedAppDefinitions = null;
            _cachedAppSnapshot = null;
            _collectionDefinitions = null;
            _collectionsSource = null;
            _currentSteamId = null;
        }
    }

    private AppDefinitionsCache LoadAppDefinitions()
    {
        _familySharingCache.Clear();

        var dependencies = new Dictionary<string, CacheDependency>(StringComparer.OrdinalIgnoreCase);
        var loginUsersPath = Path.Combine(_steamDirectory, "config", "loginusers.vdf");
        TrackDependency(dependencies, loginUsersPath, DependencyKind.File);

        if (!_files.FileExists(loginUsersPath))
        {
            return new AppDefinitionsCache(new Dictionary<uint, SteamAppDefinition>(), null, new CacheSnapshot(dependencies.Values.ToArray()));
        }

        var loginUsers = _textParser.Parse(_files.ReadAllText(loginUsersPath));
        var usersNode = loginUsers.FindPath("users") ?? FindChildCaseInsensitive(loginUsers, "users");
        if (usersNode is null)
        {
            return new AppDefinitionsCache(new Dictionary<uint, SteamAppDefinition>(), null, new CacheSnapshot(dependencies.Values.ToArray()));
        }

        var steamId = FindMostRecentUser(usersNode);
        if (steamId is null)
        {
            return new AppDefinitionsCache(new Dictionary<uint, SteamAppDefinition>(), null, new CacheSnapshot(dependencies.Values.ToArray()));
        }

        var definitions = LoadDefinitionsFromLocalConfig(steamId, dependencies);
        AddLibraryCacheEntries(steamId, definitions, dependencies);
        var collections = LoadCollectionsFromSharedConfig(steamId, dependencies);

        foreach (var (appId, appCollections) in collections)
        {
            UpsertDefinition(definitions, appId, null, null, null, appCollections);
        }

        ApplyAppMetadata(definitions);

        return new AppDefinitionsCache(definitions, steamId, new CacheSnapshot(dependencies.Values.ToArray()));
    }

    private Dictionary<uint, SteamAppDefinition> LoadDefinitionsFromLocalConfig(string steamId, IDictionary<string, CacheDependency> dependencies)
    {
        var definitions = new Dictionary<uint, SteamAppDefinition>();

        foreach (var candidate in GetUserDirectoryCandidates(steamId))
        {
            var localConfigPath = Path.Combine(_steamDirectory, "userdata", candidate, "config", "localconfig.vdf");
            TrackDependency(dependencies, localConfigPath, DependencyKind.File);
            if (!_files.FileExists(localConfigPath))
            {
                continue;
            }

            var localConfig = _textParser.Parse(_files.ReadAllText(localConfigPath));
            var storeNode = localConfig.FindPath("UserLocalConfigStore") ?? FindChildCaseInsensitive(localConfig, "UserLocalConfigStore");
            if (storeNode is null)
            {
                continue;
            }

            var appsNode = storeNode.FindPath("apps") ?? FindChildCaseInsensitive(storeNode, "apps");
            if (appsNode is null)
            {
                continue;
            }

            foreach (var (key, value) in appsNode.Children)
            {
                if (!uint.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
                {
                    continue;
                }

                string? name = null;
                if (value.TryGetChild("name", out var nameNode))
                {
                    name = nameNode.Value;
                }

                bool? isInstalled = null;
                var installedNode = FindChildCaseInsensitive(value, "Installed") ?? FindChildCaseInsensitive(value, "installed");
                if (installedNode is not null && installedNode.TryGetBoolean(out var installedFlag))
                {
                    isInstalled = installedFlag;
                }

                string? type = null;
                var typeNode = FindChildCaseInsensitive(value, "AppType") ?? FindChildCaseInsensitive(value, "type");
                if (typeNode is not null && !string.IsNullOrWhiteSpace(typeNode.Value))
                {
                    type = typeNode.Value;
                }

                if (TryFindBooleanFlag(value, "IsSubscribedFromFamilySharing", out var familyShared))
                {
                    _familySharingCache[appId] = familyShared;
                }

                UpsertDefinition(definitions, appId, string.IsNullOrWhiteSpace(name) ? null : name, isInstalled ?? true, type, null);
            }
        }

        return definitions;
    }

    private void AddLibraryCacheEntries(string steamId, Dictionary<uint, SteamAppDefinition> definitions, IDictionary<string, CacheDependency> dependencies)
    {
        foreach (var candidate in GetUserDirectoryCandidates(steamId))
        {
            var libraryCachePath = Path.Combine(_steamDirectory, "userdata", candidate, "config", "librarycache");
            TrackDependency(dependencies, libraryCachePath, DependencyKind.Directory);
            if (!Directory.Exists(libraryCachePath))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(libraryCachePath, "*.json", SearchOption.TopDirectoryOnly))
            {
                TrackDependency(dependencies, path, DependencyKind.File);
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (!uint.TryParse(fileName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
                {
                    continue;
                }

                if (appId == 0)
                {
                    continue;
                }

                UpsertDefinition(definitions, appId, null, null, null, null);
            }
        }
    }

    private Dictionary<uint, IReadOnlyList<string>> LoadCollectionsFromSharedConfig(string steamId, IDictionary<string, CacheDependency> dependencies)
    {
        var result = new Dictionary<uint, HashSet<string>>();

        foreach (var candidate in GetUserDirectoryCandidates(steamId))
        {
            var sharedConfigPath = Path.Combine(_steamDirectory, "userdata", candidate, "7", "remote", "sharedconfig.vdf");
            TrackDependency(dependencies, sharedConfigPath, DependencyKind.File);
            if (!_files.FileExists(sharedConfigPath))
            {
                continue;
            }

            var sharedConfig = _textParser.Parse(_files.ReadAllText(sharedConfigPath));
            var storeNode = sharedConfig.FindPath("UserRoamingConfigStore") ?? FindChildCaseInsensitive(sharedConfig, "UserRoamingConfigStore");
            if (storeNode is null)
            {
                continue;
            }

            var tagLookup = new Dictionary<string, string>(StringComparer.Ordinal);
            var tagsNode = FindChildCaseInsensitive(storeNode, "tags");
            if (tagsNode is not null)
            {
                foreach (var (tagId, tagNode) in tagsNode.Children)
                {
                    var tagName = ResolveTagName(tagNode);
                    if (!string.IsNullOrWhiteSpace(tagName))
                    {
                        tagLookup[tagId] = tagName!;
                    }
                }
            }

            var appsNode = FindChildCaseInsensitive(storeNode, "apps");
            if (appsNode is null)
            {
                continue;
            }

            foreach (var (appIdText, appNode) in appsNode.Children)
            {
                if (!uint.TryParse(appIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
                {
                    continue;
                }

                var appTagsNode = FindChildCaseInsensitive(appNode, "tags");
                if (appTagsNode is null)
                {
                    continue;
                }

                if (!result.TryGetValue(appId, out var categories))
                {
                    categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[appId] = categories;
                }

                foreach (var (tagId, tagNode) in appTagsNode.Children)
                {
                    var category = ResolveCategoryName(tagId, tagNode, tagLookup);
                    if (!string.IsNullOrWhiteSpace(category))
                    {
                        categories.Add(category);
                    }
                }
            }

            var collectionsNode = FindChildCaseInsensitive(storeNode, "collections");
            if (collectionsNode is not null)
            {
                foreach (var collectionNode in collectionsNode.Children.Values)
                {
                    var collectionName = ResolveCollectionDisplayName(collectionNode);
                    if (string.IsNullOrWhiteSpace(collectionName))
                    {
                        continue;
                    }

                    var appIds = CollectAppIdsFromCollection(collectionNode);
                    foreach (var appId in appIds)
                    {
                        if (!result.TryGetValue(appId, out var categories))
                        {
                            categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            result[appId] = categories;
                        }

                        categories.Add(collectionName);
                    }
                }
            }
        }

        return result.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.ToArray());
    }

    private static string? ResolveTagName(ValveKeyValueNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Value))
        {
            return node.Value;
        }

        var tagNode = FindChildCaseInsensitive(node, "tag");
        if (tagNode is not null && !string.IsNullOrWhiteSpace(tagNode.Value))
        {
            return tagNode.Value;
        }

        return null;
    }

    private static string? ResolveCategoryName(string tagId, ValveKeyValueNode node, IDictionary<string, string> lookup)
    {
        if (lookup.TryGetValue(tagId, out var name))
        {
            return name;
        }

        var candidate = node.Value;
        if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "0", StringComparison.Ordinal) && !string.Equals(candidate, "1", StringComparison.Ordinal))
        {
            return candidate;
        }

        var tagNode = FindChildCaseInsensitive(node, "tag");
        if (tagNode is not null && !string.IsNullOrWhiteSpace(tagNode.Value))
        {
            return tagNode.Value;
        }

        return tagId;
    }

    private static string? ResolveCollectionDisplayName(ValveKeyValueNode collectionNode)
    {
        var displayNode = FindChildCaseInsensitive(collectionNode, "display_name")
            ?? FindChildCaseInsensitive(collectionNode, "name")
            ?? FindChildCaseInsensitive(collectionNode, "custom_name")
            ?? FindChildCaseInsensitive(collectionNode, "localized_name");

        if (displayNode is not null && !string.IsNullOrWhiteSpace(displayNode.Value))
        {
            return displayNode.Value;
        }

        if (!string.IsNullOrWhiteSpace(collectionNode.Value))
        {
            return collectionNode.Value;
        }

        return null;
    }

    private static IReadOnlyCollection<uint> CollectAppIdsFromCollection(ValveKeyValueNode collectionNode)
    {
        var appIds = new HashSet<uint>();
        CollectAppIdsRecursive(collectionNode, appIds, isMembershipContext: false);
        return appIds;
    }

    private static readonly HashSet<string> CollectionMembershipNodeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "apps",
        "appids",
        "app_ids",
        "added",
        "add",
        "members",
        "children",
        "included",
        "includedapps",
        "app_list",
        "applist",
        "appidslist",
        "appidlist",
    };

    private static void CollectAppIdsRecursive(ValveKeyValueNode node, ISet<uint> appIds, bool isMembershipContext)
    {
        var currentIsMembership = isMembershipContext || CollectionMembershipNodeNames.Contains(node.Name);

        if (currentIsMembership && !string.IsNullOrWhiteSpace(node.Value) &&
            uint.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valueAppId))
        {
            appIds.Add(valueAppId);
        }

        foreach (var child in node.Children.Values)
        {
            var childIsMembership = currentIsMembership || CollectionMembershipNodeNames.Contains(child.Name);

            if (childIsMembership &&
                string.IsNullOrWhiteSpace(child.Value) &&
                !child.IsObject &&
                uint.TryParse(child.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keyAppId))
            {
                appIds.Add(keyAppId);
            }

            CollectAppIdsRecursive(child, appIds, childIsMembership);
        }
    }

    public bool IsSubscribedFromFamilySharing(uint appId)
    {
        EnsureAppInfoMetadataLoaded();

        return _familySharingCache.TryGetValue(appId, out var isFamilyShared) && isFamilyShared;
    }

    private void EnsureAppInfoMetadataLoaded()
    {
        if (_appInfoLoaded)
        {
            return;
        }

        var appInfoPath = Path.Combine(_steamDirectory, "appcache", "appinfo.vdf");
        if (!_files.FileExists(appInfoPath))
        {
            _appInfoLoaded = true;
            return;
        }

        try
        {
            using var stream = _files.OpenRead(appInfoPath);
            var entries = _binaryParser.ParseAppInfo(stream);
            foreach (var (appId, node) in entries)
            {
                if (TryGetFamilySharingFlag(node, out var flag))
                {
                    _familySharingCache[appId] = flag;
                }

                if (TryGetAppName(node, out var appName))
                {
                    _appNames[appId] = appName;
                }

                if (TryGetAppType(node, out var appType))
                {
                    _appTypes[appId] = appType;
                }

                var categories = ExtractStoreCategories(node);
                if (categories.Count > 0)
                {
                    _appCategories[appId] = categories;
                }

                if (TryGetDeckCompatibility(node, out var compatibility))
                {
                    _appDeckCompatibility[appId] = compatibility;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or KeyValueException or ZstdException)
        {
            // Ignore appinfo parsing errors and continue with limited metadata.
        }
        finally
        {
            _appInfoLoaded = true;
        }
    }

    private static bool TryGetFamilySharingFlag(ValveKeyValueNode node, out bool flag)
    {
        var stack = new Stack<ValveKeyValueNode>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (NameMatchesFlag(current.Name, "IsSubscribedFromFamilySharing") &&
                current.TryGetBoolean(out flag))
            {
                return true;
            }

            foreach (var child in current.Children.Values)
            {
                stack.Push(child);
            }
        }

        flag = false;
        return false;
    }

    private static bool TryFindBooleanFlag(ValveKeyValueNode node, string flagName, out bool flag)
    {
        var stack = new Stack<ValveKeyValueNode>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (NameMatchesFlag(current.Name, flagName) && current.TryGetBoolean(out flag))
            {
                return true;
            }

            foreach (var child in current.Children.Values)
            {
                stack.Push(child);
            }
        }

        flag = false;
        return false;
    }

    private static bool NameMatchesFlag(string candidate, string expected)
    {
        var normalizedCandidate = NormalizeFlag(candidate);
        var normalizedExpected = NormalizeFlag(expected);
        return string.Equals(normalizedCandidate, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFlag(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        if (builder.Length > 0 && builder[0] == 'b')
        {
            builder.Remove(0, 1);
        }

        return builder.ToString();
    }

    private bool TryGetAppName(ValveKeyValueNode node, out string name)
    {
        var common = FindChildCaseInsensitive(node, "common");
        if (common is null)
        {
            name = string.Empty;
            return false;
        }

        var nameNode = FindChildCaseInsensitive(common, "name");
        if (nameNode is not null && !string.IsNullOrWhiteSpace(nameNode.Value))
        {
            name = nameNode.Value;
            return true;
        }

        name = string.Empty;
        return false;
    }

    private bool TryGetAppType(ValveKeyValueNode node, out string type)
    {
        var common = FindChildCaseInsensitive(node, "common");
        if (common is null)
        {
            type = string.Empty;
            return false;
        }

        var typeNode = FindChildCaseInsensitive(common, "type");
        if (typeNode is not null && !string.IsNullOrWhiteSpace(typeNode.Value))
        {
            type = typeNode.Value;
            return true;
        }

        type = string.Empty;
        return false;
    }

    private static string? FindMostRecentUser(ValveKeyValueNode usersNode)
    {
        string? candidate = null;
        DateTimeOffset mostRecentTimestamp = DateTimeOffset.MinValue;

        foreach (var (steamId, node) in usersNode.Children)
        {
            var timestampNode = FindChildCaseInsensitive(node, "Timestamp");
            if (timestampNode is not null &&
                long.TryParse(timestampNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
            {
                var userTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                if (userTime > mostRecentTimestamp)
                {
                    mostRecentTimestamp = userTime;
                    candidate = steamId;
                }
            }

            var mostRecentNode = FindChildCaseInsensitive(node, "MostRecent");
            if (mostRecentNode is not null &&
                mostRecentNode.TryGetBoolean(out var isMostRecent) && isMostRecent)
            {
                return steamId;
            }
        }

        return candidate;
    }

    private void ApplyAppMetadata(Dictionary<uint, SteamAppDefinition> definitions)
    {
        EnsureAppInfoMetadataLoaded();
        foreach (var (appId, definition) in definitions.ToArray())
        {
            var updated = definition;

            if (string.IsNullOrWhiteSpace(updated.Name) &&
                _appNames.TryGetValue(appId, out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                updated = updated with { Name = name };
            }

            if (string.IsNullOrWhiteSpace(updated.Type) &&
                _appTypes.TryGetValue(appId, out var type) &&
                !string.IsNullOrWhiteSpace(type))
            {
                updated = updated with { Type = type };
            }

            if (_appCategories.TryGetValue(appId, out var categoryList))
            {
                updated = updated with { StoreCategoryIds = categoryList };
            }

            if (_appDeckCompatibility.TryGetValue(appId, out var deckCompatibility))
            {
                updated = updated with { DeckCompatibility = deckCompatibility };
            }

            definitions[appId] = updated;
        }
    }

    private static IEnumerable<string> GetUserDirectoryCandidates(string steamId)
    {
        yield return steamId;

        if (ulong.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamIdValue) &&
            steamIdValue >= SteamIdOffset)
        {
            var accountId = steamIdValue - SteamIdOffset;
            var accountIdString = accountId.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(accountIdString, steamId, StringComparison.Ordinal))
            {
                yield return accountIdString;
            }
        }
    }

    private void UpsertDefinition(
        Dictionary<uint, SteamAppDefinition> definitions,
        uint appId,
        string? name,
        bool? isInstalled,
        string? type,
        IReadOnlyList<string>? collections)
    {
        if (definitions.TryGetValue(appId, out var existing))
        {
            var updatedName = !string.IsNullOrWhiteSpace(name) ? name : existing.Name;
            var updatedInstalled = isInstalled.HasValue ? (isInstalled.Value || existing.IsInstalled) : existing.IsInstalled;
            var updatedType = !string.IsNullOrWhiteSpace(type) ? type : existing.Type;
            IReadOnlyList<string> updatedCollections;
            if (collections is null || collections.Count == 0)
            {
                updatedCollections = existing.Collections;
            }
            else if (existing.Collections.Count == 0)
            {
                updatedCollections = collections;
            }
            else
            {
                updatedCollections = existing.Collections
                    .Concat(collections)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            definitions[appId] = existing with
            {
                Name = updatedName,
                IsInstalled = updatedInstalled,
                Type = updatedType,
                Collections = updatedCollections
            };
        }
        else
        {
            definitions[appId] = new SteamAppDefinition(
                appId,
                string.IsNullOrWhiteSpace(name) ? null : name,
                isInstalled ?? false,
                string.IsNullOrWhiteSpace(type) ? null : type,
                collections ?? Array.Empty<string>());
        }
    }

    private static ValveKeyValueNode? FindChildCaseInsensitive(ValveKeyValueNode parent, string name)
    {
        foreach (var (key, child) in parent.Children)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private const ulong SteamIdOffset = 76561197960265728UL;

    private IReadOnlyList<SteamCollectionDefinition> LoadCollectionsFromCloudStorage(string steamId)
    {
        var results = new Dictionary<string, (SteamCollectionDefinition Definition, long Timestamp)>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in GetUserDirectoryCandidates(steamId))
        {
            var cloudStoragePath = Path.Combine(_steamDirectory, "userdata", candidate, "config", "cloudstorage", "cloud-storage-namespace-1.json");
            if (!_files.FileExists(cloudStoragePath))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                var json = _files.ReadAllText(cloudStoragePath);
                document = JsonDocument.Parse(json);
            }
            catch (IOException)
            {
                continue;
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                foreach (var entry in document.RootElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 2)
                    {
                        continue;
                    }

                    var key = entry[0].GetString();
                    if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("user-collections", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var payload = entry[1];
                    if (payload.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var timestamp = payload.TryGetProperty("timestamp", out var timestampElement) && timestampElement.ValueKind == JsonValueKind.Number && timestampElement.TryGetInt64(out var ts)
                        ? ts
                        : 0;

                    if (!payload.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var rawValue = valueElement.GetString();
                    if (string.IsNullOrWhiteSpace(rawValue))
                    {
                        continue;
                    }

                    try
                    {
                        using var valueDocument = JsonDocument.Parse(rawValue);
                        var root = valueDocument.RootElement;

                        if (!root.TryGetProperty("id", out var idElement))
                        {
                            continue;
                        }

                        var id = idElement.GetString();
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                            ? nameElement.GetString() ?? id
                            : id;

                        var explicitAppIds = ParseAppIdSet(root, "added");
                        var filterSpec = ParseFilterSpec(root);

                        var definition = new SteamCollectionDefinition(id, name, explicitAppIds, filterSpec);

                        if (results.TryGetValue(id, out var existing) && existing.Timestamp >= timestamp)
                        {
                            continue;
                        }

                        results[id] = (definition, timestamp);
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed user collection entries.
                    }
                }
            }
        }

        return results.Values
            .OrderBy(entry => entry.Definition.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => entry.Definition)
            .ToArray();
    }
    private static IReadOnlyCollection<uint> ParseAppIdSet(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<uint>();
        }

        var set = new HashSet<uint>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetUInt32(out var value))
            {
                set.Add(value);
            }
        }

        return set.Count == 0 ? Array.Empty<uint>() : set.ToArray();
    }

    private static CollectionFilterSpec? ParseFilterSpec(JsonElement root)
    {
        if (!root.TryGetProperty("filterSpec", out var specElement) || specElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (!specElement.TryGetProperty("filterGroups", out var groupsElement) || groupsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var groups = new List<CollectionFilterGroup>();
        foreach (var groupElement in groupsElement.EnumerateArray())
        {
            if (groupElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var options = new List<int>();
            if (groupElement.TryGetProperty("rgOptions", out var optionsElement) && optionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var optionElement in optionsElement.EnumerateArray())
                {
                    if (optionElement.TryGetInt32(out var option))
                    {
                        options.Add(option);
                    }
                }
            }

            if (options.Count == 0)
            {
                continue;
            }

            var acceptUnion = groupElement.TryGetProperty("bAcceptUnion", out var acceptUnionElement) && acceptUnionElement.ValueKind == JsonValueKind.True;

            groups.Add(new CollectionFilterGroup(options.ToArray(), acceptUnion));
        }

        if (groups.Count == 0)
        {
            return null;
        }

        return new CollectionFilterSpec(groups.ToArray());
    }
    private static IReadOnlyList<int> ExtractStoreCategories(ValveKeyValueNode node)
    {
        var common = FindChildCaseInsensitive(node, "common");
        if (common is null)
        {
            return Array.Empty<int>();
        }

        var categoryNode = FindChildCaseInsensitive(common, "category");
        if (categoryNode is null)
        {
            return Array.Empty<int>();
        }

        var categories = new List<int>();
        foreach (var (key, child) in categoryNode.Children)
        {
            if (key is null)
            {
                continue;
            }

            if (!key.StartsWith("category_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(key.AsSpan("category_".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var categoryId))
            {
                continue;
            }

            if (child.TryGetBoolean(out var flag) && !flag)
            {
                continue;
            }

            categories.Add(categoryId);
        }

        if (categories.Count == 0)
        {
            return Array.Empty<int>();
        }

        categories.Sort();
        return categories;
    }

    private static bool TryGetDeckCompatibility(ValveKeyValueNode node, out SteamDeckCompatibility compatibility)
    {
        var common = FindChildCaseInsensitive(node, "common");
        if (common is null)
        {
            compatibility = SteamDeckCompatibility.Unknown;
            return false;
        }

        var deck = FindChildCaseInsensitive(common, "steam_deck_compatibility");
        if (deck is null)
        {
            compatibility = SteamDeckCompatibility.Unknown;
            return false;
        }

        var categoryNode = FindChildCaseInsensitive(deck, "category") ?? FindChildCaseInsensitive(deck, "overall_category");
        if (categoryNode?.Value is null)
        {
            compatibility = SteamDeckCompatibility.Unknown;
            return false;
        }

        if (!int.TryParse(categoryNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            compatibility = SteamDeckCompatibility.Unknown;
            return false;
        }

        compatibility = value switch
        {
            1 => SteamDeckCompatibility.Verified,
            2 => SteamDeckCompatibility.Playable,
            3 => SteamDeckCompatibility.Unsupported,
            _ => SteamDeckCompatibility.Unknown,
        };

        return compatibility != SteamDeckCompatibility.Unknown;
    }

    private static bool CacheIsFresh(CacheSnapshot snapshot)
    {
        foreach (var dependency in snapshot.Dependencies)
        {
            if (!IsDependencyCurrent(dependency))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDependencyCurrent(CacheDependency dependency)
    {
        try
        {
            var exists = dependency.Kind == DependencyKind.File
                ? File.Exists(dependency.Path)
                : Directory.Exists(dependency.Path);

            if (exists != dependency.Exists)
            {
                return false;
            }

            if (!exists)
            {
                return true;
            }

            var currentTimestamp = dependency.Kind == DependencyKind.File
                ? File.GetLastWriteTimeUtc(dependency.Path)
                : Directory.GetLastWriteTimeUtc(dependency.Path);

            return currentTimestamp == dependency.LastWriteTimeUtc;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TrackDependency(
        IDictionary<string, CacheDependency> dependencies,
        string path,
        DependencyKind kind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        dependencies[path] = CreateDependency(path, kind);
    }

    private static CacheDependency CreateDependency(string path, DependencyKind kind)
    {
        bool exists;
        DateTime lastWriteTimeUtc;

        try
        {
            if (kind == DependencyKind.File)
            {
                exists = File.Exists(path);
                lastWriteTimeUtc = exists ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            else
            {
                exists = Directory.Exists(path);
                lastWriteTimeUtc = exists ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
        }
        catch (IOException)
        {
            exists = false;
            lastWriteTimeUtc = DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            exists = false;
            lastWriteTimeUtc = DateTime.MinValue;
        }

        return new CacheDependency(path, kind, exists, lastWriteTimeUtc);
    }

    private enum DependencyKind
    {
        File,
        Directory
    }

    private sealed record CacheSnapshot(IReadOnlyList<CacheDependency> Dependencies);

    private sealed record CacheDependency(string Path, DependencyKind Kind, bool Exists, DateTime LastWriteTimeUtc);

    private sealed record AppDefinitionsCache(
        IReadOnlyDictionary<uint, SteamAppDefinition> Definitions,
        string? SteamId,
        CacheSnapshot Snapshot);
}






