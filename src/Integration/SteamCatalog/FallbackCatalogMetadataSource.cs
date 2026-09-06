namespace SteamCatalog;

/// <summary>Uses PICS when available, with a short circuit break to avoid reconnecting once per app during outages.</summary>
public sealed class FallbackCatalogMetadataSource : ICatalogMetadataSource
{
    private readonly ICatalogMetadataSource _primary;
    private readonly ICatalogMetadataSource _fallback;
    private readonly TimeProvider _time;
    private long _retryPrimaryAfterTicks;

    public FallbackCatalogMetadataSource(ICatalogMetadataSource primary, ICatalogMetadataSource fallback, TimeProvider? timeProvider = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<MetadataResponse> FetchAsync(uint appId, string language, CancellationToken cancellationToken)
    {
        if (_time.GetUtcNow().UtcTicks >= Interlocked.Read(ref _retryPrimaryAfterTicks))
        {
            var primary = await _primary.FetchAsync(appId, language, cancellationToken).ConfigureAwait(false);
            if (primary.Status == CatalogLookupStatus.Found) return primary;
            if (primary.Status == CatalogLookupStatus.Unavailable)
                Interlocked.Exchange(ref _retryPrimaryAfterTicks, _time.GetUtcNow().AddMinutes(1).UtcTicks);
        }
        return await _fallback.FetchAsync(appId, language, cancellationToken).ConfigureAwait(false);
    }
}
