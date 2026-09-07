using SteamCatalog;

namespace SteamCatalog.Tests;

internal sealed class CacheFixture : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SteamCatalog.Tests", Guid.NewGuid().ToString("N"));
    public CacheFixture() => Cache = new SqliteCatalogCache(Path.Combine(_directory, "catalog.db"));
    public SqliteCatalogCache Cache { get; }
    public void Dispose()
    {
        Cache.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

internal sealed class DelegateSource(Func<uint, string, CancellationToken, Task<MetadataResponse>> fetch) : ICatalogMetadataSource
{
    private int _calls;
    public int Calls => _calls;
    public Task<MetadataResponse> FetchAsync(uint appId, string language, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return fetch(appId, language, cancellationToken);
    }
    public static MetadataResponse Found(uint id, string language) => new(CatalogLookupStatus.Found,
        new CatalogItem { AppId = id, Language = language, Name = $"Game {id} {language}", Source = "fixture" });
}

internal sealed class FakeFamilyTransport : ISteamFamilyTransport
{
    public SteamSessionIdentity? CurrentIdentity { get; set; }
    public int FetchCalls { get; private set; }
    public Func<ulong, string, CancellationToken, Task<FamilySnapshot>> Fetch { get; set; } = (id, language, _) =>
        Task.FromResult(new FamilySnapshot { SteamId = id, Language = language, Status = FamilySnapshotStatus.Success });
    public Func<IProgress<SteamLoginProgress>?, CancellationToken, Task<SteamSessionIdentity>> Login { get; set; } = (_, _) =>
        Task.FromResult(new SteamSessionIdentity(11, "fixture"));
    public async Task<SteamSessionIdentity> LoginWithQrAsync(IProgress<SteamLoginProgress>? progress, CancellationToken cancellationToken)
    {
        CurrentIdentity = await Login(progress, cancellationToken);
        return CurrentIdentity;
    }
    public Task LogoutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentIdentity = null;
        return Task.CompletedTask;
    }
    public Task<FamilySnapshot> FetchFamilyAsync(ulong steamId, string language, CancellationToken cancellationToken)
    {
        FetchCalls++;
        return Fetch(steamId, language, cancellationToken);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal sealed class CountingBatchCache(SqliteCatalogCache inner) : ICatalogCache, IBatchCatalogCache
{
    public int BatchReads { get; private set; }
    public int SingleReads { get; private set; }
    public bool FailBatch { get; init; }
    public Task<IReadOnlyDictionary<uint, CachedCatalogEntry>> ReadManyAsync(IReadOnlyCollection<uint> ids, string language, CancellationToken token = default)
    {
        BatchReads++;
        return FailBatch ? Task.FromException<IReadOnlyDictionary<uint, CachedCatalogEntry>>(new IOException("Fixture bulk failure."))
            : inner.ReadManyAsync(ids, language, token);
    }
    public Task<CachedCatalogEntry?> ReadAsync(uint id, string language, CancellationToken token = default)
    {
        SingleReads++;
        return inner.ReadAsync(id, language, token);
    }
    public Task WriteAsync(CachedCatalogEntry entry, CancellationToken token = default) => inner.WriteAsync(entry, token);
    public Task<FamilySnapshot?> ReadFamilyAsync(ulong id, string language, CancellationToken token = default) => inner.ReadFamilyAsync(id, language, token);
    public Task WriteFamilyAsync(FamilySnapshot snapshot, CancellationToken token = default) => inner.WriteFamilyAsync(snapshot, token);
    public Task DeleteFamilyAsync(ulong id, CancellationToken token = default) => inner.DeleteFamilyAsync(id, token);
}
