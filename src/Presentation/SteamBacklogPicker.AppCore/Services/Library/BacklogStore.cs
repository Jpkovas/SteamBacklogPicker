using System.Globalization;
using System.Text.Json;
using Domain;

namespace SteamBacklogPicker.UI.Services.Library;

public enum BacklogStatus
{
    None = 0,
    WantToPlay = 1,
    Playing = 2,
    Completed = 3,
    Hidden = 4,
    Later = 5
}

public sealed record BacklogHistory(uint AppId, string Title, DateTimeOffset SelectedAt);

public sealed class BacklogAccount
{
    public bool AvoidRepeats { get; set; } = true;
    public Dictionary<uint, BacklogStatus> Games { get; set; } = new();
    public List<BacklogHistory> History { get; set; } = new();
}

/// <summary>User decisions are isolated from imported metadata and keyed by Steam account.</summary>
public sealed class BacklogStore
{
    public const int HistoryLimit = 200;
    private const int MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxTitleCharacters = 512;
    private const ulong SteamIdOffset = 76561197960265728UL;
    private readonly object _syncRoot = new();
    private readonly string? _path;
    private readonly Dictionary<string, BacklogAccount> _accounts;

    public BacklogStore(string? path = null)
    {
        _path = path is null ? null : Path.GetFullPath(path);
        _accounts = Load();
    }

    public BacklogStatus GetStatus(string accountId, uint appId)
    {
        var key = NormalizeAccountId(accountId);
        lock (_syncRoot)
        {
            return key is not null && _accounts.TryGetValue(key, out var account)
                ? account.Games.GetValueOrDefault(appId) : BacklogStatus.None;
        }
    }

    public IReadOnlyList<BacklogHistory> GetHistory(string accountId)
    {
        var key = NormalizeAccountId(accountId);
        lock (_syncRoot)
        {
            return key is not null && _accounts.TryGetValue(key, out var account)
                ? account.History.ToArray() : Array.Empty<BacklogHistory>();
        }
    }

    public bool GetAvoidRepeats(string accountId)
    {
        var key = NormalizeAccountId(accountId);
        lock (_syncRoot)
        {
            return key is null || !_accounts.TryGetValue(key, out var account) || account.AvoidRepeats;
        }
    }

    public void SetAvoidRepeats(string accountId, bool value)
    {
        var key = NormalizeAccountId(accountId);
        if (key is not null) UpdateAccount(key, account => account.AvoidRepeats = value);
    }

    public void SetStatus(string accountId, uint appId, BacklogStatus status)
    {
        var key = NormalizeAccountId(accountId);
        if (key is null || appId == 0 || !Enum.IsDefined(status)) return;
        UpdateAccount(key, account =>
        {
            if (status == BacklogStatus.None) account.Games.Remove(appId);
            else account.Games[appId] = status;
        });
    }

    public void RecordDraw(string accountId, GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var key = NormalizeAccountId(accountId);
        if (key is null || game.Id.Storefront != Storefront.Steam || game.SteamAppId is not uint id || id == 0) return;
        UpdateAccount(key, account =>
        {
            account.History.Insert(0, new(id, NormalizeTitle(game.Title, id), DateTimeOffset.UtcNow));
            if (account.History.Count > HistoryLimit)
                account.History.RemoveRange(HistoryLimit, account.History.Count - HistoryLimit);
        });
    }

    public void ClearHistory(string accountId)
    {
        var key = NormalizeAccountId(accountId);
        if (key is not null) UpdateAccount(key, account => account.History.Clear());
    }

    private void UpdateAccount(string key, Action<BacklogAccount> update)
    {
        lock (_syncRoot)
        {
            var next = _accounts.TryGetValue(key, out var current)
                ? new BacklogAccount { Games = new(current.Games), History = new(current.History), AvoidRepeats = current.AvoidRepeats }
                : new BacklogAccount();
            update(next);
            var nextAccounts = new Dictionary<string, BacklogAccount>(_accounts, StringComparer.Ordinal) { [key] = next };
            // Do not report an in-memory decision as saved when the disk commit failed.
            Save(nextAccounts);
            _accounts[key] = next;
        }
    }

    private Dictionary<string, BacklogAccount> Load()
    {
        var accounts = new Dictionary<string, BacklogAccount>(StringComparer.Ordinal);
        if (_path is null || !File.Exists(_path)) return accounts;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxFileBytes) return accounts;
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) != 0)
            {
                if (buffer.Length + read > MaxFileBytes) return accounts;
                buffer.Write(chunk, 0, read);
            }
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return accounts;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = NormalizeAccountId(property.Name);
                if (key is null || property.Value.ValueKind != JsonValueKind.Object) continue;
                var account = new BacklogAccount();
                if (property.Value.TryGetProperty("AvoidRepeats", out var avoidRepeats) &&
                    avoidRepeats.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    account.AvoidRepeats = avoidRepeats.GetBoolean();
                if (property.Value.TryGetProperty("Games", out var games) && games.ValueKind == JsonValueKind.Object)
                {
                    foreach (var game in games.EnumerateObject())
                    {
                        if (uint.TryParse(game.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0 &&
                            game.Value.ValueKind == JsonValueKind.Number && game.Value.TryGetInt32(out var status) &&
                            Enum.IsDefined((BacklogStatus)status) && status != 0)
                        {
                            account.Games[id] = (BacklogStatus)status;
                        }
                    }
                }
                if (property.Value.TryGetProperty("History", out var history) && history.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in history.EnumerateArray())
                    {
                        if (account.History.Count >= HistoryLimit) break;
                        if (entry.ValueKind != JsonValueKind.Object ||
                            !entry.TryGetProperty("AppId", out var appId) || appId.ValueKind != JsonValueKind.Number ||
                            !appId.TryGetUInt32(out var id) || id == 0 ||
                            !entry.TryGetProperty("Title", out var title) || title.ValueKind != JsonValueKind.String ||
                            !entry.TryGetProperty("SelectedAt", out var selectedAt) || selectedAt.ValueKind != JsonValueKind.String ||
                            !selectedAt.TryGetDateTimeOffset(out var timestamp)) continue;
                        account.History.Add(new(id, NormalizeTitle(title.GetString(), id), timestamp));
                    }
                }
                accounts[key] = account;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged local document must not prevent the application from opening.
            accounts.Clear();
        }
        return accounts;
    }

    private void Save(Dictionary<string, BacklogAccount> accounts)
    {
        if (_path is null) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(accounts);
        if (bytes.Length > MaxFileBytes) throw new IOException("Backlog storage exceeds the supported size limit.");
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string? NormalizeAccountId(string? accountId)
    {
        if (string.Equals(accountId, "local", StringComparison.Ordinal)) return "local";
        if (!ulong.TryParse(accountId, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId) ||
            steamId <= SteamIdOffset || steamId - SteamIdOffset > uint.MaxValue) return null;
        return steamId.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeTitle(string? title, uint appId)
    {
        var value = string.IsNullOrWhiteSpace(title) ? $"App {appId}" : title.Trim();
        return value.Length <= MaxTitleCharacters ? value : value[..MaxTitleCharacters];
    }
}
