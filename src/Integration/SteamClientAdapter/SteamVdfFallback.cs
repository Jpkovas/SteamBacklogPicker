using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ValveFormatParser;

namespace SteamClientAdapter;

public interface ISteamVdfFallback
{
    IReadOnlyCollection<uint> GetInstalledAppIds();

    bool IsSubscribedFromFamilySharing(uint appId);
}

public sealed class SteamVdfFallback : ISteamVdfFallback
{
    private readonly string _steamDirectory;
    private readonly IFileAccessor _files;
    private readonly ValveTextVdfParser _textParser;
    private readonly ValveBinaryVdfParser _binaryParser;
    private IReadOnlyCollection<uint>? _cachedAppIds;
    private readonly ConcurrentDictionary<uint, bool> _familySharingCache = new();
    private bool _familySharingLoaded;

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
        if (_cachedAppIds is not null)
        {
            return _cachedAppIds;
        }

        var loginUsersPath = Path.Combine(_steamDirectory, "config", "loginusers.vdf");
        if (!_files.FileExists(loginUsersPath))
        {
            _cachedAppIds = Array.Empty<uint>();
            return _cachedAppIds;
        }

        var loginUsers = _textParser.Parse(_files.ReadAllText(loginUsersPath));
        var usersNode = loginUsers.FindPath("users");
        if (usersNode is null)
        {
            _cachedAppIds = Array.Empty<uint>();
            return _cachedAppIds;
        }

        var steamId = FindMostRecentUser(usersNode);
        if (steamId is null)
        {
            _cachedAppIds = Array.Empty<uint>();
            return _cachedAppIds;
        }

        var localConfigPath = Path.Combine(_steamDirectory, "userdata", steamId, "config", "localconfig.vdf");
        if (!_files.FileExists(localConfigPath))
        {
            _cachedAppIds = Array.Empty<uint>();
            return _cachedAppIds;
        }

        var localConfig = _textParser.Parse(_files.ReadAllText(localConfigPath));
        var appsNode = localConfig.FindPath("UserLocalConfigStore", "apps");
        if (appsNode is null)
        {
            _cachedAppIds = Array.Empty<uint>();
            return _cachedAppIds;
        }

        var appIds = new SortedSet<uint>();
        foreach (var (key, value) in appsNode.Children)
        {
            if (!uint.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
            {
                continue;
            }

            if (value.TryGetChild("Installed", out var installedNode) || value.TryGetChild("installed", out installedNode))
            {
                if (installedNode.TryGetBoolean(out var isInstalled) && !isInstalled)
                {
                    continue;
                }
            }

            appIds.Add(appId);
        }

        _cachedAppIds = appIds.ToArray();
        return _cachedAppIds;
    }

    public bool IsSubscribedFromFamilySharing(uint appId)
    {
        if (!_familySharingLoaded)
        {
            LoadFamilySharingData();
        }

        return _familySharingCache.TryGetValue(appId, out var isFamilyShared) && isFamilyShared;
    }

    private void LoadFamilySharingData()
    {
        var appInfoPath = Path.Combine(_steamDirectory, "appcache", "appinfo.vdf");
        if (!_files.FileExists(appInfoPath))
        {
            _familySharingLoaded = true;
            return;
        }

        using var stream = _files.OpenRead(appInfoPath);
        var entries = _binaryParser.ParseAppInfo(stream);
        foreach (var (appId, node) in entries)
        {
            var flagNode = node.FindPath("extended", "IsSubscribedFromFamilySharing");
            if (flagNode is not null && TryGetBooleanValue(flagNode, out var flag))
            {
                _familySharingCache[appId] = flag;
            }
        }

        _familySharingLoaded = true;
    }

    private static bool TryGetBooleanValue(ValveKeyValueNode node, out bool value)
    {
        if (node.TryGetBoolean(out value))
        {
            return true;
        }

        if (node.TryGetChild("value", out var valueNode) && valueNode.TryGetBoolean(out value))
        {
            return true;
        }

        var raw = node.Value;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            raw = raw.Trim();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                value = numeric != 0;
                return true;
            }

            if (bool.TryParse(raw, out value))
            {
                return true;
            }
        }

        value = false;
        return false;
    }

    private static string? FindMostRecentUser(ValveKeyValueNode usersNode)
    {
        string? candidate = null;
        DateTimeOffset mostRecentTimestamp = DateTimeOffset.MinValue;

        foreach (var (steamId, node) in usersNode.Children)
        {
            if (node.TryGetChild("Timestamp", out var timestampNode) &&
                long.TryParse(timestampNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
            {
                var userTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                if (userTime > mostRecentTimestamp)
                {
                    mostRecentTimestamp = userTime;
                    candidate = steamId;
                }
            }

            if (node.TryGetChild("MostRecent", out var mostRecentNode) &&
                mostRecentNode.TryGetBoolean(out var isMostRecent) && isMostRecent)
            {
                return steamId;
            }
        }

        return candidate;
    }
}
