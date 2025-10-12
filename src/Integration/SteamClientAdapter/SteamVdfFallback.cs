using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ValveFormatParser;
using ValveKeyValue;
using ZstdSharp;

namespace SteamClientAdapter;

public interface ISteamVdfFallback
{
    IReadOnlyCollection<uint> GetInstalledAppIds();

    bool IsSubscribedFromFamilySharing(uint appId);

    IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps();
}

public sealed class SteamVdfFallback : ISteamVdfFallback
{
    private readonly string _steamDirectory;
    private readonly IFileAccessor _files;
    private readonly ValveTextVdfParser _textParser;
    private readonly ValveBinaryVdfParser _binaryParser;
    private readonly ConcurrentDictionary<uint, bool> _familySharingCache = new();
    private readonly Dictionary<uint, string> _appNames = new();
    private bool _appInfoLoaded;

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
        return LoadAppDefinitions();
    }

    private IReadOnlyDictionary<uint, SteamAppDefinition> LoadAppDefinitions()
    {
        var loginUsersPath = Path.Combine(_steamDirectory, "config", "loginusers.vdf");
        if (!_files.FileExists(loginUsersPath))
        {
            return new Dictionary<uint, SteamAppDefinition>();
        }

        var loginUsers = _textParser.Parse(_files.ReadAllText(loginUsersPath));
        var usersNode = loginUsers.FindPath("users") ?? FindChildCaseInsensitive(loginUsers, "users");
        if (usersNode is null)
        {
            return new Dictionary<uint, SteamAppDefinition>();
        }

        var steamId = FindMostRecentUser(usersNode);
        if (steamId is null)
        {
            return new Dictionary<uint, SteamAppDefinition>();
        }

        var definitions = LoadDefinitionsFromLocalConfig(steamId);
        AddLibraryCacheEntries(steamId, definitions);
        var collections = LoadCollectionsFromSharedConfig(steamId);

        foreach (var (appId, appCollections) in collections)
        {
            UpsertDefinition(definitions, appId, null, null, appCollections);
        }

        ApplyAppNames(definitions);

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

                UpsertDefinition(definitions, appId, string.IsNullOrWhiteSpace(name) ? null : name, isInstalled ?? true, null);
            }
        }

        return definitions;
    }

    private void AddLibraryCacheEntries(string steamId, Dictionary<uint, SteamAppDefinition> definitions)
    {
        foreach (var candidate in GetUserDirectoryCandidates(steamId))
        {
            var libraryCachePath = Path.Combine(_steamDirectory, "userdata", candidate, "config", "librarycache");
            if (!Directory.Exists(libraryCachePath))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(libraryCachePath, "*.json", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (!uint.TryParse(fileName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
                {
                    continue;
                }

                if (appId == 0)
                {
                    continue;
                }

                UpsertDefinition(definitions, appId, null, null, null);
            }
        }
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
            if (string.Equals(current.Name, "IsSubscribedFromFamilySharing", StringComparison.Ordinal) &&
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

    private void ApplyAppNames(Dictionary<uint, SteamAppDefinition> definitions)
    {
        EnsureAppInfoMetadataLoaded();
        foreach (var (appId, definition) in definitions.ToArray())
        {
            if (definition.Name is not null)
            {
                continue;
            }

            if (_appNames.TryGetValue(appId, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                definitions[appId] = definition with { Name = name };
            }
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
        IReadOnlyList<string>? collections)
    {
        if (definitions.TryGetValue(appId, out var existing))
        {
            var updatedName = !string.IsNullOrWhiteSpace(name) ? name : existing.Name;
            var updatedInstalled = isInstalled.HasValue ? (isInstalled.Value || existing.IsInstalled) : existing.IsInstalled;
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
                Collections = updatedCollections
            };
        }
        else
        {
            definitions[appId] = new SteamAppDefinition(
                appId,
                string.IsNullOrWhiteSpace(name) ? null : name,
                isInstalled ?? false,
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
}
