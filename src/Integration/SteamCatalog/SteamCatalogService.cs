using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace SteamCatalog;

public sealed class SteamCatalogService : ISteamCatalogService, IDisposable
{
    private readonly ICatalogCache _cache;
    private readonly ICatalogMetadataSource _source;
    private readonly CatalogOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _networkGate;
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;
    // Fixed stripes avoid an ever-growing keyed-lock dictionary while providing single-flight per app/language.
    private readonly SemaphoreSlim[] _lookupGates = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private DateTimeOffset _nextRequest;

    public SteamCatalogService(ICatalogCache cache, ICatalogMetadataSource source,
        CatalogOptions? options = null, TimeProvider? timeProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? new CatalogOptions();
        _options.Validate();
        _time = timeProvider ?? TimeProvider.System;
        _networkGate = new SemaphoreSlim(_options.MaxConcurrency);
    }

    public async Task<IReadOnlyDictionary<uint, CatalogLookupResult>> EnrichAsync(
        IEnumerable<uint> appIds, string language = "english", CatalogNetworkMode mode = CatalogNetworkMode.Offline,
        IProgress<CatalogProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appIds);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        var normalized = CatalogLanguage.Normalize(language);
        var ids = new HashSet<uint>();
        foreach (var appId in appIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (appId != 0) ids.Add(appId);
            if (ids.Count > _options.MaxAppsPerRequest)
                throw new ArgumentException("The catalog request exceeds the configured app limit.", nameof(appIds));
        }
        IReadOnlyDictionary<uint, CachedCatalogEntry>? batchCache = null;
        if (ids.Count > 0 && _cache is IBatchCatalogCache bulk)
        {
            try { batchCache = await bulk.ReadManyAsync(ids, normalized, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException) { }
        }
        var results = new ConcurrentDictionary<uint, CatalogLookupResult>();
        var completed = 0;
        await Parallel.ForEachAsync(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxConcurrency,
            CancellationToken = cancellationToken
        }, async (id, token) =>
        {
            CatalogLookupResult result;
            if (batchCache is not null && batchCache.TryGetValue(id, out var cached)
                && (mode == CatalogNetworkMode.Offline || cached.ExpiresAt > _time.GetUtcNow()))
                result = new CatalogLookupResult(id, cached.Status, cached.Item, true, cached.ExpiresAt <= _time.GetUtcNow());
            else if (batchCache is not null && mode == CatalogNetworkMode.Offline)
                result = new CatalogLookupResult(id, CatalogLookupStatus.Unavailable, ErrorCode: "offline_cache_miss");
            else
                // Recheck stale/missing online entries inside the shared lock to preserve global single-flight.
                result = await LookupAsync(id, normalized, mode, token).ConfigureAwait(false);
            results[id] = result;
            progress?.Report(new CatalogProgress(Interlocked.Increment(ref completed), ids.Count, id, result.Status));
        }).ConfigureAwait(false);
        return results.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private async Task<CatalogLookupResult> LookupAsync(uint appId, string language, CatalogNetworkMode mode, CancellationToken token)
    {
        if (mode == CatalogNetworkMode.Offline)
            return await LookupCoreAsync(appId, language, mode, token).ConfigureAwait(false);
        var gate = _lookupGates[(HashCode.Combine(appId, language) & int.MaxValue) % _lookupGates.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await LookupCoreAsync(appId, language, mode, token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task<CatalogLookupResult> LookupCoreAsync(uint appId, string language, CatalogNetworkMode mode, CancellationToken token)
    {
        CachedCatalogEntry? cached = null;
        try { cached = await _cache.ReadAsync(appId, language, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException) { }

        var isFresh = cached is not null && cached.ExpiresAt > _time.GetUtcNow();
        if (cached is not null && (isFresh || mode == CatalogNetworkMode.Offline))
            return new CatalogLookupResult(appId, cached.Status, cached.Item, true, !isFresh);
        if (mode == CatalogNetworkMode.Offline)
            return new CatalogLookupResult(appId, CatalogLookupStatus.Unavailable, ErrorCode: "offline_cache_miss");

        var response = await FetchWithRetryAsync(appId, language, token).ConfigureAwait(false);
        if (response.Status == CatalogLookupStatus.Found
            && (response.Item?.AppId != appId || response.Item.Language != language || string.IsNullOrWhiteSpace(response.Item.Name)))
            response = new MetadataResponse(CatalogLookupStatus.Unavailable, ErrorCode: "invalid_metadata_identity");

        if (response.Status != CatalogLookupStatus.Unavailable)
        {
            var now = _time.GetUtcNow();
            var ttl = response.Status == CatalogLookupStatus.Found ? _options.PositiveTtl : _options.NegativeTtl;
            try
            {
                await _cache.WriteAsync(new CachedCatalogEntry(appId, language, response.Status, response.Item, now, now + ttl), token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException) { }
            return new CatalogLookupResult(appId, response.Status, response.Item);
        }
        if (cached?.Status == CatalogLookupStatus.Found)
            return new CatalogLookupResult(appId, cached.Status, cached.Item, true, true, response.ErrorCode);
        return new CatalogLookupResult(appId, CatalogLookupStatus.Unavailable, ErrorCode: response.ErrorCode);
    }

    private async Task<MetadataResponse> FetchWithRetryAsync(uint appId, string language, CancellationToken token)
    {
        await _networkGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await WaitForRateLimitAsync(token).ConfigureAwait(false);
                MetadataResponse response;
                try
                {
                    response = await _source.FetchAsync(appId, language, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { response = new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, ErrorCode: "metadata_timeout"); }
                catch (HttpRequestException)
                { response = new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, ErrorCode: "metadata_network_error"); }
                if (!response.Retryable || attempt >= _options.MaxRetries) return response;
                var backoff = TimeSpan.FromMilliseconds(_options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                var delay = response.RetryAfter ?? backoff;
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                if (delay > _options.MaxRetryDelay) delay = _options.MaxRetryDelay;
                await Task.Delay(delay, _time, token).ConfigureAwait(false);
            }
        }
        finally { _networkGate.Release(); }
    }

    private async Task WaitForRateLimitAsync(CancellationToken token)
    {
        await _rateGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var delay = _nextRequest - _time.GetUtcNow();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, _time, token).ConfigureAwait(false);
            _nextRequest = _time.GetUtcNow() + _options.MinimumRequestInterval;
        }
        finally { _rateGate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        // In-flight operations still release these managed gates in finally blocks. They and the
        // untimed cancellation source are collected after the last operation; disposal never blocks shutdown.
    }
}
