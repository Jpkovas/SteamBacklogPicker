namespace SteamCatalog;

public enum CatalogNetworkMode { Offline, Online }
public enum CatalogLookupStatus { Found, NotFound, Unavailable }

[Flags]
public enum CatalogPlatforms { Unknown = 0, Windows = 1, Linux = 2, MacOS = 4 }

public sealed record class CatalogItem
{
    public uint AppId { get; init; }
    public string Language { get; init; } = "english";
    public string Name { get; init; } = string.Empty;
    public string ProductType { get; init; } = "unknown";
    public CatalogPlatforms Platforms { get; init; }
    public string? HeaderImageUrl { get; init; }
    public string Source { get; init; } = string.Empty;
}

public sealed record class CatalogLookupResult(
    uint AppId, CatalogLookupStatus Status, CatalogItem? Item = null,
    bool IsFromCache = false, bool IsStale = false, string? ErrorCode = null);

public sealed record class CatalogProgress(int Completed, int Total, uint AppId, CatalogLookupStatus Status);

public interface ISteamCatalogService
{
    Task<IReadOnlyDictionary<uint, CatalogLookupResult>> EnrichAsync(
        IEnumerable<uint> appIds, string language = "english", CatalogNetworkMode mode = CatalogNetworkMode.Offline,
        IProgress<CatalogProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record class MetadataResponse(
    CatalogLookupStatus Status, CatalogItem? Item = null, bool Retryable = false,
    TimeSpan? RetryAfter = null, string? ErrorCode = null);

public interface ICatalogMetadataSource
{
    Task<MetadataResponse> FetchAsync(uint appId, string language, CancellationToken cancellationToken);
}

public sealed record class CachedCatalogEntry(
    uint AppId, string Language, CatalogLookupStatus Status, CatalogItem? Item,
    DateTimeOffset UpdatedAt, DateTimeOffset ExpiresAt);

public interface ICatalogCache
{
    Task<CachedCatalogEntry?> ReadAsync(uint appId, string language, CancellationToken cancellationToken = default);
    Task WriteAsync(CachedCatalogEntry entry, CancellationToken cancellationToken = default);
    Task<FamilySnapshot?> ReadFamilyAsync(ulong steamId, string language, CancellationToken cancellationToken = default);
    Task WriteFamilyAsync(FamilySnapshot snapshot, CancellationToken cancellationToken = default);
    Task DeleteFamilyAsync(ulong steamId, CancellationToken cancellationToken = default);
}

/// <summary>Optional bulk-read capability. Missing IDs are absent from the returned dictionary.</summary>
public interface IBatchCatalogCache
{
    Task<IReadOnlyDictionary<uint, CachedCatalogEntry>> ReadManyAsync(
        IReadOnlyCollection<uint> appIds, string language, CancellationToken cancellationToken = default);
}

public sealed record class CatalogOptions
{
    public int MaxConcurrency { get; init; } = 2;
    public int MaxAppsPerRequest { get; init; } = 50_000;
    public int MaxRetries { get; init; } = 2;
    public TimeSpan PositiveTtl { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan NegativeTtl { get; init; } = TimeSpan.FromHours(6);
    public TimeSpan MinimumRequestInterval { get; init; } = TimeSpan.FromMilliseconds(350);
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaxConcurrency is < 1 or > 8 || MaxAppsPerRequest is < 1 or > 100_000 || MaxRetries is < 0 or > 5
            || PositiveTtl <= TimeSpan.Zero || NegativeTtl <= TimeSpan.Zero || MinimumRequestInterval < TimeSpan.Zero
            || RetryBaseDelay < TimeSpan.Zero || MaxRetryDelay < RetryBaseDelay || MaxRetryDelay > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(CatalogOptions));
    }
}

public static class CatalogLanguage
{
    public static string Normalize(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        var normalized = language.Trim().ToLowerInvariant();
        if (normalized.Length > 32 || normalized.Any(c => !char.IsAsciiLetter(c) && c != '-'))
            throw new ArgumentException("Use a Steam language name such as english or brazilian.", nameof(language));
        return normalized;
    }
}
