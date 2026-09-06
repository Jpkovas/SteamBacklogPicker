using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SteamCatalog;

/// <summary>App-owned metadata only. Steam installation caches and authentication tokens are never written here.</summary>
public sealed class SqliteCatalogCache : ICatalogCache, IBatchCatalogCache, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;
    private bool _initialized;

    public SqliteCatalogCache(string? path = null)
    {
        _path = Path.GetFullPath(path ?? GetDefaultPath());
    }

    public string DatabasePath => _path;

    public static string GetDefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile))
                throw new InvalidOperationException("A user-owned cache directory is required.");
            root = Path.Combine(profile, ".local", "share");
        }
        return Path.Combine(root, "SteamBacklogPicker", "catalog", "catalog.db");
    }

    public async Task<CachedCatalogEntry?> ReadAsync(uint appId, string language, CancellationToken cancellationToken = default)
    {
        var normalized = CatalogLanguage.Normalize(language);
        return await WithConnectionAsync(async (connection, token) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM catalog WHERE app_id = $id AND language = $language";
            command.Parameters.AddWithValue("$id", appId);
            command.Parameters.AddWithValue("$language", normalized);
            var payload = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
            return ParseEntry(payload, appId, normalized);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyDictionary<uint, CachedCatalogEntry>> ReadManyAsync(
        IReadOnlyCollection<uint> appIds, string language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appIds);
        var normalized = CatalogLanguage.Normalize(language);
        if (appIds.Count > 100_000) throw new ArgumentException("The cache read exceeds the supported app limit.", nameof(appIds));
        var ids = appIds.Where(id => id > 0).Distinct().ToArray();
        return WithConnectionAsync<IReadOnlyDictionary<uint, CachedCatalogEntry>>(async (connection, token) =>
        {
            var entries = new Dictionary<uint, CachedCatalogEntry>();
            // Stay below SQLite parameter limits while reusing one connection for the entire refresh.
            foreach (var batch in ids.Chunk(256))
            {
                token.ThrowIfCancellationRequested();
                using var command = connection.CreateCommand();
                var parameters = new string[batch.Length];
                for (var index = 0; index < batch.Length; index++)
                {
                    parameters[index] = "$id" + index.ToString(CultureInfo.InvariantCulture);
                    command.Parameters.AddWithValue(parameters[index], batch[index]);
                }
                command.CommandText = "SELECT app_id, payload FROM catalog WHERE language = $language AND app_id IN ("
                    + string.Join(',', parameters) + ")";
                command.Parameters.AddWithValue("$language", normalized);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    var id = checked((uint)reader.GetInt64(0));
                    var entry = ParseEntry(reader.GetString(1), id, normalized);
                    if (entry is not null) entries[id] = entry;
                }
            }
            return entries;
        }, cancellationToken);
    }

    private static CachedCatalogEntry? ParseEntry(string? payload, uint appId, string language)
    {
        if (payload is null) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<CachedCatalogEntry>(payload);
            return entry?.AppId == appId && entry.Language == language
                && entry.Status is CatalogLookupStatus.Found or CatalogLookupStatus.NotFound
                && (entry.Status != CatalogLookupStatus.Found || entry.Item?.AppId == appId && entry.Item.Language == language)
                ? entry : null;
        }
        catch (JsonException) { return null; }
    }

    public Task WriteAsync(CachedCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Status == CatalogLookupStatus.Unavailable || entry.AppId == 0)
            throw new ArgumentException("Only confirmed metadata or confirmed misses can be cached.", nameof(entry));
        var normalized = CatalogLanguage.Normalize(entry.Language);
        return WithConnectionAsync(async (connection, token) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO catalog(app_id, language, payload) VALUES($id, $language, $payload) "
                + "ON CONFLICT(app_id, language) DO UPDATE SET payload = excluded.payload";
            command.Parameters.AddWithValue("$id", entry.AppId);
            command.Parameters.AddWithValue("$language", normalized);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(entry with { Language = normalized }));
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public async Task<FamilySnapshot?> ReadFamilyAsync(ulong steamId, string language, CancellationToken cancellationToken = default)
    {
        var normalized = CatalogLanguage.Normalize(language);
        return await WithConnectionAsync(async (connection, token) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM family_snapshots WHERE steam_id = $id AND language = $language";
            command.Parameters.AddWithValue("$id", steamId.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$language", normalized);
            var payload = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
            if (payload is null) return null;
            try
            {
                var snapshot = JsonSerializer.Deserialize<FamilySnapshot>(payload);
                return snapshot?.SteamId == steamId && snapshot.Language == normalized
                    && snapshot.Status == FamilySnapshotStatus.Success ? snapshot : null;
            }
            catch (JsonException) { return null; }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task WriteFamilyAsync(FamilySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SteamId == 0 || snapshot.Status != FamilySnapshotStatus.Success || snapshot.IsStale)
            throw new ArgumentException("Only a successful authoritative snapshot can replace entitlement evidence.", nameof(snapshot));
        var normalized = CatalogLanguage.Normalize(snapshot.Language);
        return WithConnectionAsync(async (connection, token) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO family_snapshots(steam_id, language, payload) VALUES($id, $language, $payload) "
                + "ON CONFLICT(steam_id, language) DO UPDATE SET payload = excluded.payload";
            command.Parameters.AddWithValue("$id", snapshot.SteamId.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$language", normalized);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(snapshot with { Language = normalized }));
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task DeleteFamilyAsync(ulong steamId, CancellationToken cancellationToken = default)
    {
        return WithConnectionAsync(async (connection, token) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM family_snapshots WHERE steam_id = $id";
            command.Parameters.AddWithValue("$id", steamId.ToString(CultureInfo.InvariantCulture));
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }

    private async Task<T> WithConnectionAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var builder = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, DefaultTimeout = 5 };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (!_initialized)
            {
                using var schema = connection.CreateCommand();
                schema.CommandText = "CREATE TABLE IF NOT EXISTS catalog (app_id INTEGER NOT NULL, language TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(app_id, language));"
                    + "CREATE TABLE IF NOT EXISTS family_snapshots (steam_id TEXT NOT NULL, language TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(steam_id, language));";
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                _initialized = true;
            }
            var result = await operation(connection, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        // Connections are scoped to operations. Keep the managed gate alive for their finally releases.
    }
}
