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
    private readonly Dictionary<uint, IReadOnlyList<SteamPlatform>> _appSupportedPlatforms = new();
    private IReadOnlyList<SteamCollectionDefinition>? _collectionDefinitions;
    private readonly object _syncRoot = new();
    private string? _snapshotVersion;
    private bool _snapshotReadFailed;
    private string? _appInfoVersion;
    private IReadOnlyDictionary<uint, SteamAppDefinition> _knownApps = new Dictionary<uint, SteamAppDefinition>();

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
        // Profile flags describe cached history, not installation. Manifests and
        // the native installation API are consumed by SteamAppManifestCache.
        return Array.Empty<uint>();
    }

    public IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps()
    {
        lock (_syncRoot)
        {
            _snapshotReadFailed = false;
            var steamId = ResolveCurrentSteamId();
            var version = BuildSnapshotVersion(steamId);
            if (version is not null && version == _snapshotVersion)
            {
                return _knownApps;
            }

            _familySharingCache.Clear();
            _collectionDefinitions = Array.Empty<SteamCollectionDefinition>();
            _knownApps = steamId is null
                ? new Dictionary<uint, SteamAppDefinition>()
                : LoadAppDefinitions(steamId);
            _snapshotVersion = _snapshotReadFailed ? null : version;
            return _knownApps;
        }
    }

    public string? GetCurrentUserSteamId()
    {
        lock (_syncRoot)
        {
            // Re-resolve rather than returning the previous account after Steam switches users.
            return ResolveCurrentSteamId();
        }
    }

    public IReadOnlyList<SteamCollectionDefinition> GetCollections()
    {
        lock (_syncRoot)
        {
            _ = GetKnownApps();
            return _collectionDefinitions ?? Array.Empty<SteamCollectionDefinition>();
        }
    }

    private string? ResolveCurrentSteamId()
    {
        var path = Path.Combine(_steamDirectory, "config", "loginusers.vdf");
        if (!TryParseTextVdfFile(path, out var root))
        {
            return null;
        }

        var users = FindChildCaseInsensitive(root, "users");
        return users is null ? null : FindMostRecentUser(users);
    }

    private string? BuildSnapshotVersion(string? steamId)
    {
        var paths = new List<string>
        {
            Path.Combine(_steamDirectory, "config", "loginusers.vdf"),
            Path.Combine(_steamDirectory, "appcache", "appinfo.vdf")
        };
        if (steamId is not null)
        {
            foreach (var candidate in GetUserDirectoryCandidates(steamId))
            {
                var user = Path.Combine(_steamDirectory, "userdata", candidate);
                paths.Add(Path.Combine(user, "config", "localconfig.vdf"));
                paths.Add(Path.Combine(user, "7", "remote", "sharedconfig.vdf"));
                paths.Add(Path.Combine(user, "config", "cloudstorage", "cloud-storage-namespace-1.json"));
                paths.AddRange(_files.EnumerateFiles(Path.Combine(user, "config", "librarycache"), "*.json"));
            }
        }

        var version = new StringBuilder(steamId ?? "unknown");
        foreach (var path in paths.OrderBy(path => path, StringComparer.Ordinal))
        {
            var stamp = _files.GetFileVersion(path);
            if (stamp is null || (stamp == "missing" && _files.FileExists(path)))
            {
                return null;
            }
            version.Append('|').Append(path).Append(':').Append(stamp);
        }
        return version.ToString();
    }

    private IReadOnlyDictionary<uint, SteamAppDefinition> LoadAppDefinitions(string steamId)
    {
        var definitions = LoadDefinitionsFromLocalConfig(steamId);
        AddLibraryCacheEntries(steamId, definitions);
        foreach (var (appId, collections) in LoadCollectionsFromSharedConfig(steamId))
        {
            UpsertDefinition(definitions, appId, null, null, null, collections);
        }

        _collectionDefinitions = LoadCollectionsFromCloudStorage(steamId);
        foreach (var collection in _collectionDefinitions)
        {
            foreach (var appId in collection.ExplicitAppIds)
            {
                UpsertDefinition(definitions, appId, null, null, null, new[] { collection.Name });
            }
        }

        ApplyAppMetadata(definitions);
        foreach (var (appId, definition) in definitions.ToArray())
        {
            if (_familySharingCache.TryGetValue(appId, out var shared) && shared)
            {
                definitions[appId] = definition with { OwnershipType = OwnershipType.FamilyShared };
            }
        }
        return definitions;
    }

    private Dictionary<uint, SteamAppDefinition> LoadDefinitionsFromLocalConfig(string steamId)
    {
        var definitions = new Dictionary<uint, SteamAppDefinition>();

        foreach (var candidate in GetUserDirectoryCandidates(steamId))
        {
            var localConfigPath = Path.Combine(_steamDirectory, "userdata", candidate, "config", "localconfig.vdf");
            if (!_files.FileExists(localConfigPath))
            {
                continue;
            }

            if (!TryParseTextVdfFile(localConfigPath, out var localConfig))
            {
                continue;
            }

            var storeNode = localConfig.FindPath("UserLocalConfigStore") ?? FindChildCaseInsensitive(localConfig, "UserLocalConfigStore");
            if (storeNode is null)
            {
                continue;
            }

            var appsNode = FindLargestNumericChildrenNode(storeNode, "apps");
            if (appsNode is null)
            {
                continue;
            }

            foreach (var (key, value) in appsNode.Children)
            {
                if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0)
                {
                    continue;
                }

                string? name = null;
                if (value.TryGetChild("name", out var nameNode))
                {
                    name = nameNode.Value;
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

                UpsertDefinition(definitions, appId, string.IsNullOrWhiteSpace(name) ? null : name, false, type, null);
                if ((TryFindBooleanFlag(value, "is_owned", out var owned) || TryFindBooleanFlag(value, "IsSubscribed", out owned)) && owned)
                {
                    definitions[appId] = definitions[appId] with { OwnershipType = OwnershipType.Owned };
                }
            }
        }

        return definitions;
    }

    private void AddLibraryCacheEntries(string steamId, Dictionary<uint, SteamAppDefinition> definitions)
    {
        foreach (var candidate in GetUserDirectoryCandidates(steamId))
        {
            var libraryCachePath = Path.Combine(_steamDirectory, "userdata", candidate, "config", "librarycache");
            foreach (var path in _files.EnumerateFiles(libraryCachePath, "*.json"))
            {
                if (!uint.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0)
                {
                    continue;
                }

                UpsertDefinition(definitions, appId, null, null, null, null);
                try
                {
                    using var document = ReadJsonDocument(path);
                    var name = FindJsonValue(document.RootElement, "name", "app_name", "display_name", "strName");
                    var type = FindJsonValue(document.RootElement, "app_type", "type");
                    UpsertDefinition(definitions, appId,
                        name is { ValueKind: JsonValueKind.String } ? name.Value.GetString() : null,
                        false,
                        type is { ValueKind: JsonValueKind.String } ? type.Value.GetString() : null,
                        null);
                    var shared = FindJsonValue(document.RootElement, "IsSubscribedFromFamilySharing", "is_family_shared");
                    var owned = FindJsonValue(document.RootElement, "is_owned", "IsSubscribed");
                    if (JsonBoolean(shared) == true)
                    {
                        _familySharingCache[appId] = true;
                    }
                    else if (JsonBoolean(owned) == true)
                    {
                        definitions[appId] = definitions[appId] with { OwnershipType = OwnershipType.Owned };
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    _snapshotReadFailed = true;
                    // A partial Steam cache must not discard unrelated entries.
                }
            }
        }
    }

    private JsonDocument ReadJsonDocument(string path)
    {
        const int maxBytes = 16 * 1024 * 1024;
        using var stream = _files.OpenRead(path);
        if (stream.CanSeek && stream.Length > maxBytes) throw new InvalidDataException("Steam JSON cache exceeds the size limit.");
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) != 0)
        {
            if (buffer.Length + read > maxBytes) throw new InvalidDataException("Steam JSON cache exceeds the size limit.");
            buffer.Write(chunk, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
    }

    private static readonly HashSet<string> AppMetadataContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "data", "appinfo", "common", "overview", "app_overview"
    };

    private static JsonElement? FindJsonValue(JsonElement root, params string[] names)
    {
        foreach (var metadata in EnumerateAppMetadataObjects(root))
        {
            foreach (var property in metadata.EnumerateObject())
            {
                if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) return property.Value;
            }
        }
        return null;
    }

    private static IEnumerable<JsonElement> EnumerateAppMetadataObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
            foreach (var property in root.EnumerateObject())
            {
                if (!AppMetadataContainers.Contains(property.Name)) continue;
                foreach (var metadata in EnumerateAppMetadataObjects(property.Value)) yield return metadata;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            // Steam also stores named cache sections as [name, payload] pairs.
            foreach (var section in root.EnumerateArray())
            {
                if (section.ValueKind != JsonValueKind.Array || section.GetArrayLength() != 2 ||
                    section[0].ValueKind != JsonValueKind.String ||
                    !AppMetadataContainers.Contains(section[0].GetString()!)) continue;
                foreach (var metadata in EnumerateAppMetadataObjects(section[1])) yield return metadata;
            }
        }
    }

    private static bool? JsonBoolean(JsonElement? value)
    {
        if (value is not { } element) return null;
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var number) => number != 0,
            _ => null
        };
    }

    private Dictionary<uint, IReadOnlyList<string>> LoadCollectionsFromSharedConfig(string steamId)
    {
        var result = new Dictionary<uint, HashSet<string>>();

        foreach (var candidate in GetUserDirectoryCandidates(steamId))
        {
            var sharedConfigPath = Path.Combine(_steamDirectory, "userdata", candidate, "7", "remote", "sharedconfig.vdf");
            if (!_files.FileExists(sharedConfigPath))
            {
                continue;
            }

            if (!TryParseTextVdfFile(sharedConfigPath, out var sharedConfig))
            {
                continue;
            }

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
            foreach (var (appIdText, appNode) in appsNode?.Children ?? new Dictionary<string, ValveKeyValueNode>())
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
        lock (_syncRoot)
        {
            return GetKnownApps().TryGetValue(appId, out var app) && app.OwnershipType == OwnershipType.FamilyShared;
        }
    }

    private void EnsureAppInfoMetadataLoaded()
    {
        var appInfoPath = Path.Combine(_steamDirectory, "appcache", "appinfo.vdf");
        var version = _files.GetFileVersion(appInfoPath);
        if (version is not null && version == _appInfoVersion) return;
        _appNames.Clear();
        _appTypes.Clear();
        _appCategories.Clear();
        _appDeckCompatibility.Clear();
        _appSupportedPlatforms.Clear();
        if (!_files.FileExists(appInfoPath))
        {
            return;
        }

        var loaded = false;
        try
        {
            using var stream = _files.OpenRead(appInfoPath);
            var entries = _binaryParser.ParseAppInfo(stream);
            foreach (var (appId, node) in entries)
            {
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

                var platforms = ExtractSupportedPlatforms(node);
                if (platforms.Count > 0)
                {
                    _appSupportedPlatforms[appId] = platforms;
                }
            }

            loaded = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or KeyValueException or ZstdException)
        {
            _snapshotReadFailed = true;
            // Ignore appinfo parsing errors and continue with limited metadata.
        }

        _appInfoVersion = loaded ? version : null;
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

        foreach (var (rawSteamId, node) in usersNode.Children)
        {
            if (!ulong.TryParse(rawSteamId, NumberStyles.None, CultureInfo.InvariantCulture, out var steamIdValue) ||
                steamIdValue <= SteamIdOffset || steamIdValue - SteamIdOffset > uint.MaxValue)
            {
                continue;
            }
            var steamId = steamIdValue.ToString(CultureInfo.InvariantCulture);
            var timestampNode = FindChildCaseInsensitive(node, "Timestamp");
            if (timestampNode is not null &&
                long.TryParse(timestampNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
            {
                DateTimeOffset userTime;
                try
                {
                    userTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

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

            if (_appNames.TryGetValue(appId, out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                updated = updated with { Name = name };
            }

            if (_appTypes.TryGetValue(appId, out var type) &&
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

            if (_appSupportedPlatforms.TryGetValue(appId, out var supportedPlatforms))
            {
                updated = updated with { SupportedPlatforms = supportedPlatforms };
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
        if (appId == 0) return;
        if (definitions.TryGetValue(appId, out var existing))
        {
            var updatedName = !string.IsNullOrWhiteSpace(name) ? name : existing.Name;
            var updatedInstalled = false;
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
                false,
                string.IsNullOrWhiteSpace(type) ? null : type,
                collections ?? Array.Empty<string>()) { InstallState = InstallState.Available };
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

    private static ValveKeyValueNode? FindLargestNumericChildrenNode(ValveKeyValueNode parent, string name)
    {
        ValveKeyValueNode? best = null;
        var bestCount = -1;

        void Visit(ValveKeyValueNode current)
        {
            if (string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                var numericChildren = current.Children.Keys.Count(static key =>
                    uint.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));

                if (numericChildren > bestCount)
                {
                    best = current;
                    bestCount = numericChildren;
                }
            }

            foreach (var child in current.Children.Values)
            {
                Visit(child);
            }
        }

        Visit(parent);
        return best;
    }

    private bool TryParseTextVdfFile(string path, out ValveKeyValueNode root)
    {
        root = default!;

        if (!_files.FileExists(path))
        {
            return false;
        }

        try
        {
            using var stream = _files.OpenRead(path);
            root = _textParser.Parse(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _snapshotReadFailed = true;
            return false;
        }
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
                document = ReadJsonDocument(cloudStoragePath);
            }
            catch (IOException)
            {
                _snapshotReadFailed = true;
                continue;
            }
            catch (Exception ex) when (ex is JsonException or UnauthorizedAccessException)
            {
                _snapshotReadFailed = true;
                continue;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var entry in document.RootElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 2)
                    {
                        continue;
                    }

                    if (entry[0].ValueKind != JsonValueKind.String) continue;
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

                        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
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
        if (!root.TryGetProperty("filterSpec", out var specElement) || specElement.ValueKind != JsonValueKind.Object)
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
                    if (optionElement.ValueKind == JsonValueKind.Number && optionElement.TryGetInt32(out var option))
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
            1 => SteamDeckCompatibility.Unsupported,
            2 => SteamDeckCompatibility.Playable,
            3 => SteamDeckCompatibility.Verified,
            _ => SteamDeckCompatibility.Unknown,
        };

        return compatibility != SteamDeckCompatibility.Unknown;
    }

    private static IReadOnlyList<SteamPlatform> ExtractSupportedPlatforms(ValveKeyValueNode node)
    {
        var common = FindChildCaseInsensitive(node, "common");
        if (common is null)
        {
            return Array.Empty<SteamPlatform>();
        }

        var osList = FindChildCaseInsensitive(common, "oslist");
        if (string.IsNullOrWhiteSpace(osList?.Value))
        {
            return Array.Empty<SteamPlatform>();
        }

        var platforms = new List<SteamPlatform>();
        foreach (var item in osList.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var platform = item.ToLowerInvariant() switch
            {
                "windows" or "win" => SteamPlatform.Windows,
                "macos" or "mac" or "osx" => SteamPlatform.MacOS,
                "linux" or "steamdeck" => SteamPlatform.Linux,
                _ => (SteamPlatform?)null,
            };

            if (platform.HasValue && !platforms.Contains(platform.Value))
            {
                platforms.Add(platform.Value);
            }
        }

        return platforms;
    }
}
