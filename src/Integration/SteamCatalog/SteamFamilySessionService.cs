using Microsoft.Data.Sqlite;

namespace SteamCatalog;

public sealed class SteamFamilySessionService : ISteamFamilySessionService
{
    private readonly ISteamFamilyTransport _transport;
    private readonly ICatalogCache _cache;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<ulong> _invalidatedIdentities = new();
    // A failed disk write must not resurrect older rights on the next offline/failed refresh.
    // These entries contain entitlement DTOs only; they never contain authentication credentials.
    private readonly Dictionary<(ulong SteamId, string Language), FamilySnapshot> _uncommittedSnapshots = new();

    public SteamFamilySessionService(ISteamFamilyTransport transport, ICatalogCache cache,
        TimeProvider? timeProvider = null, TimeSpan? entitlementTtl = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _time = timeProvider ?? TimeProvider.System;
        _ttl = entitlementTtl ?? TimeSpan.FromMinutes(30);
        if (_ttl <= TimeSpan.Zero || _ttl > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(entitlementTtl));
    }

    public SteamSessionIdentity? CurrentIdentity => _transport.CurrentIdentity;

    public async Task<SteamSessionIdentity> LoginWithQrAsync(IProgress<SteamLoginProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var identity = await _transport.LoginWithQrAsync(progress, cancellationToken).ConfigureAwait(false);
            // A new login alone does not revalidate old entitlement evidence; an online snapshot does.
            return identity;
        }
        finally { _gate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var identity = CurrentIdentity;
            await _transport.LogoutAsync(cancellationToken).ConfigureAwait(false);
            if (identity is not null)
            {
                _invalidatedIdentities.Add(identity.SteamId);
                ClearUncommittedSnapshots(identity.SteamId);
                // If cache deletion fails, surface it. The token is already cleared; do not claim a successful purge.
                await _cache.DeleteFamilyAsync(identity.SteamId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<FamilySnapshot> GetFamilySnapshotAsync(ulong steamId, string language = "english",
        CatalogNetworkMode mode = CatalogNetworkMode.Offline, CancellationToken cancellationToken = default)
    {
        if (steamId == 0) throw new ArgumentOutOfRangeException(nameof(steamId));
        var normalized = CatalogLanguage.Normalize(language);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (mode == CatalogNetworkMode.Offline && _invalidatedIdentities.Contains(steamId))
                return Failure(steamId, normalized, FamilySnapshotStatus.NotAuthenticated, "identity_rights_invalidated");
            FamilySnapshot? cached = null;
            var key = (steamId, normalized);
            if (!_invalidatedIdentities.Contains(steamId) && !_uncommittedSnapshots.TryGetValue(key, out cached))
            {
                try { cached = await _cache.ReadFamilyAsync(steamId, normalized, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException) { }
            }

            if (mode == CatalogNetworkMode.Offline)
                return cached is null
                    ? Failure(steamId, normalized, FamilySnapshotStatus.NotAuthenticated, "offline_no_family_snapshot")
                    : cached with { IsFromCache = true, IsStale = cached.ExpiresAt <= _time.GetUtcNow() };

            if (CurrentIdentity?.SteamId != steamId)
                return Failure(steamId, normalized, FamilySnapshotStatus.NotAuthenticated, "identity_requires_explicit_login");

            // An explicit online refresh always checks the server, including fresh cache entries.
            var result = await _transport.FetchFamilyAsync(steamId, normalized, cancellationToken).ConfigureAwait(false);
            if (result.SteamId != steamId || result.Language != normalized)
                return Failure(steamId, normalized, FamilySnapshotStatus.Error, "family_response_identity_mismatch");

            if (result.Status == FamilySnapshotStatus.Success)
            {
                var now = _time.GetUtcNow();
                result = result with { FetchedAt = now, ExpiresAt = now + _ttl, IsFromCache = false, IsStale = false };
                try
                {
                    await _cache.WriteFamilyAsync(result, cancellationToken).ConfigureAwait(false);
                    _uncommittedSnapshots.Remove(key);
                }
                catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException)
                {
                    _uncommittedSnapshots[key] = result;
                }
                _invalidatedIdentities.Remove(steamId);
                return result;
            }
            if (result.Status is FamilySnapshotStatus.Revoked or FamilySnapshotStatus.NotAuthenticated)
            {
                _invalidatedIdentities.Add(steamId);
                ClearUncommittedSnapshots(steamId);
                await DeleteCachedRightsAsync(steamId, cancellationToken).ConfigureAwait(false);
                return result with { Apps = Array.Empty<FamilyApp>() };
            }
            // Preserve last-known evidence on transport failure, while never presenting it as a fresh success.
            return cached is null ? result : cached with
            {
                Status = FamilySnapshotStatus.Error, IsFromCache = true, IsStale = true, ErrorCode = result.ErrorCode
            };
        }
        finally { _gate.Release(); }
    }

    private async Task DeleteCachedRightsAsync(ulong steamId, CancellationToken token)
    {
        try { await _cache.DeleteFamilyAsync(steamId, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException) { }
    }

    private void ClearUncommittedSnapshots(ulong steamId)
    {
        foreach (var key in _uncommittedSnapshots.Keys.Where(key => key.SteamId == steamId).ToArray())
            _uncommittedSnapshots.Remove(key);
    }

    private static FamilySnapshot Failure(ulong steamId, string language, FamilySnapshotStatus status, string code)
        => new() { SteamId = steamId, Language = language, Status = status, ErrorCode = code };
}
