using FluentAssertions;
using Xunit;

namespace SteamCatalog.Tests;

public sealed class CatalogServiceTests
{
    private static CatalogOptions FastOptions => new()
    {
        MinimumRequestInterval = TimeSpan.Zero, RetryBaseDelay = TimeSpan.Zero, MaxRetryDelay = TimeSpan.Zero
    };

    [Fact]
    public async Task EnrichAsync_ShouldUseOneBulkReadAndNoIndividualReadsForFreshOnlineAndOfflineCache()
    {
        using var fixture = new CacheFixture();
        var now = DateTimeOffset.UtcNow;
        foreach (var id in new uint[] { 10, 20, 30 })
            await fixture.Cache.WriteAsync(new(id, "english", CatalogLookupStatus.Found,
                DelegateSource.Found(id, "english").Item, now, now.AddDays(1)));
        var cache = new CountingBatchCache(fixture.Cache);
        var source = new DelegateSource((_, _, _) => throw new InvalidOperationException("Fresh cache must avoid network."));
        using var service = new SteamCatalogService(cache, source, FastOptions);

        var offline = await service.EnrichAsync([10, 20, 30, 40]);
        var online = await service.EnrichAsync([10, 20, 30], mode: CatalogNetworkMode.Online);

        offline[40].ErrorCode.Should().Be("offline_cache_miss");
        online.Values.Should().OnlyContain(result => result.IsFromCache);
        cache.BatchReads.Should().Be(2);
        cache.SingleReads.Should().Be(0);
        source.Calls.Should().Be(0);
    }

    [Fact]
    public async Task EnrichAsync_ShouldFallBackToIndividualCacheReadsIfBulkReadFails()
    {
        using var fixture = new CacheFixture();
        var now = DateTimeOffset.UtcNow;
        await fixture.Cache.WriteAsync(new(10, "english", CatalogLookupStatus.Found,
            DelegateSource.Found(10, "english").Item, now, now.AddDays(1)));
        var cache = new CountingBatchCache(fixture.Cache) { FailBatch = true };
        var source = new DelegateSource((_, _, _) => throw new InvalidOperationException("Offline must avoid network."));
        using var service = new SteamCatalogService(cache, source, FastOptions);

        var results = await service.EnrichAsync([10]);

        results[10].Item!.Name.Should().Be("Game 10 english");
        cache.BatchReads.Should().Be(1);
        cache.SingleReads.Should().Be(1);
    }

    [Fact]
    public async Task EnrichAsync_ShouldAvoidAllNetworkInOfflineModeAndDeduplicateIds()
    {
        using var fixture = new CacheFixture();
        var source = new DelegateSource((id, language, _) => Task.FromResult(DelegateSource.Found(id, language)));
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions);

        var results = await service.EnrichAsync([0, 10, 10, 20]);

        results.Keys.Should().BeEquivalentTo([10u, 20u]);
        results.Values.Should().OnlyContain(result => result.Status == CatalogLookupStatus.Unavailable && result.ErrorCode == "offline_cache_miss");
        source.Calls.Should().Be(0);
    }

    [Fact]
    public async Task EnrichAsync_ShouldPersistLanguageSpecificMetadataAcrossCacheInstances()
    {
        using var fixture = new CacheFixture();
        var source = new DelegateSource((id, language, _) => Task.FromResult(DelegateSource.Found(id, language)));
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions);
        await service.EnrichAsync([10], "english", CatalogNetworkMode.Online);
        await service.EnrichAsync([10], "brazilian", CatalogNetworkMode.Online);
        using var reopenedCache = new SqliteCatalogCache(fixture.Cache.DatabasePath);
        using var offline = new SteamCatalogService(reopenedCache, source, FastOptions);

        var english = await offline.EnrichAsync([10], "english");
        var portuguese = await offline.EnrichAsync([10], "brazilian");

        english[10].Item!.Name.Should().Be("Game 10 english");
        portuguese[10].Item!.Name.Should().Be("Game 10 brazilian");
        source.Calls.Should().Be(2);
    }

    [Fact]
    public async Task EnrichAsync_ShouldUseStaleMetadataOfflineAndRefreshAfterTtlOnline()
    {
        using var fixture = new CacheFixture();
        var time = new ManualTimeProvider();
        var source = new DelegateSource((id, language, _) => Task.FromResult(DelegateSource.Found(id, language)));
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions, time);
        await service.EnrichAsync([10], mode: CatalogNetworkMode.Online);
        time.Advance(TimeSpan.FromDays(8));

        var stale = await service.EnrichAsync([10]);
        stale[10].IsFromCache.Should().BeTrue();
        stale[10].IsStale.Should().BeTrue();
        source.Calls.Should().Be(1);

        var refreshed = await service.EnrichAsync([10], mode: CatalogNetworkMode.Online);
        refreshed[10].IsStale.Should().BeFalse();
        source.Calls.Should().Be(2);
    }

    [Fact]
    public async Task EnrichAsync_ShouldExpireNegativeCacheWithoutCachingTransientErrors()
    {
        using var fixture = new CacheFixture();
        var time = new ManualTimeProvider();
        var source = new DelegateSource((_, _, _) => Task.FromResult(new MetadataResponse(CatalogLookupStatus.NotFound)));
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions, time);
        await service.EnrichAsync([10], mode: CatalogNetworkMode.Online);
        await service.EnrichAsync([10], mode: CatalogNetworkMode.Online);
        source.Calls.Should().Be(1);
        time.Advance(TimeSpan.FromHours(7));
        await service.EnrichAsync([10], mode: CatalogNetworkMode.Online);
        source.Calls.Should().Be(2);

        var failing = new DelegateSource((_, _, _) => Task.FromResult(new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true)));
        using var failureService = new SteamCatalogService(fixture.Cache, failing, FastOptions, time);
        await failureService.EnrichAsync([20], mode: CatalogNetworkMode.Online);
        failing.Calls.Should().Be(3);
        (await fixture.Cache.ReadAsync(20, "english")).Should().BeNull();
    }

    [Fact]
    public async Task EnrichAsync_ShouldPreserveStalePositiveCacheOnServerFailure()
    {
        using var fixture = new CacheFixture();
        var time = new ManualTimeProvider();
        await fixture.Cache.WriteAsync(new CachedCatalogEntry(10, "english", CatalogLookupStatus.Found,
            DelegateSource.Found(10, "english").Item, time.GetUtcNow(), time.GetUtcNow().AddMinutes(-1)));
        var source = new DelegateSource((_, _, _) => Task.FromResult(new MetadataResponse(CatalogLookupStatus.Unavailable, ErrorCode: "server_error")));
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions, time);

        var result = (await service.EnrichAsync([10], mode: CatalogNetworkMode.Online))[10];

        result.Status.Should().Be(CatalogLookupStatus.Found);
        result.IsStale.Should().BeTrue();
        result.ErrorCode.Should().Be("server_error");
    }

    [Fact]
    public async Task EnrichAsync_ShouldBoundConcurrentRequestsAcrossOverlappingRefreshes()
    {
        using var fixture = new CacheFixture();
        var active = 0;
        var maximum = 0;
        var source = new DelegateSource(async (id, language, token) =>
        {
            var count = Interlocked.Increment(ref active);
            int previous;
            do { previous = maximum; } while (count > previous && Interlocked.CompareExchange(ref maximum, count, previous) != previous);
            try { await Task.Delay(15, token); return DelegateSource.Found(id, language); }
            finally { Interlocked.Decrement(ref active); }
        });
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions with { MaxConcurrency = 2 });

        await Task.WhenAll(service.EnrichAsync([1, 2, 3, 4], mode: CatalogNetworkMode.Online),
            service.EnrichAsync([5, 6, 7, 8], mode: CatalogNetworkMode.Online));

        maximum.Should().BeInRange(1, 2);
        source.Calls.Should().Be(8);
    }

    [Fact]
    public async Task EnrichAsync_ShouldSingleFlightTheSameAppAndLanguageAcrossConcurrentCalls()
    {
        using var fixture = new CacheFixture();
        var source = new DelegateSource(async (id, language, token) =>
        {
            await Task.Delay(40, token);
            return DelegateSource.Found(id, language);
        });
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.EnrichAsync([10], mode: CatalogNetworkMode.Online)));

        source.Calls.Should().Be(1);
        results.Should().OnlyContain(result => result[10].Status == CatalogLookupStatus.Found);
    }

    [Fact]
    public async Task EnrichAsync_ShouldCancelOneWaitingCallerWithoutCancellingTheSharedSuccessfulLookup()
    {
        using var fixture = new CacheFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new DelegateSource(async (id, language, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return DelegateSource.Found(id, language);
        });
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions);
        var original = service.EnrichAsync([10], mode: CatalogNetworkMode.Online);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var waiter = service.EnrichAsync([10], mode: CatalogNetworkMode.Online, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await FluentActions.Awaiting(() => waiter).Should().ThrowAsync<OperationCanceledException>();
        release.TrySetResult();

        (await original)[10].Status.Should().Be(CatalogLookupStatus.Found);
        source.Calls.Should().Be(1);
    }

    [Fact]
    public async Task EnrichAsync_ShouldCancelBackoffAndAvoidWritingPartialEntries()
    {
        using var fixture = new CacheFixture();
        using var cancellation = new CancellationTokenSource();
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new DelegateSource((_, _, _) =>
        {
            attempted.TrySetResult();
            return Task.FromResult(new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, RetryAfter: TimeSpan.FromSeconds(30)));
        });
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions with { MaxRetryDelay = TimeSpan.FromSeconds(30) });
        var request = service.EnrichAsync([10], mode: CatalogNetworkMode.Online, cancellationToken: cancellation.Token);
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await FluentActions.Awaiting(() => request).Should().ThrowAsync<OperationCanceledException>();
        source.Calls.Should().Be(1);
        (await fixture.Cache.ReadAsync(10, "english")).Should().BeNull();
    }

    [Fact]
    public async Task Dispose_ShouldCancelInflightAndQueuedLookupsWithoutDisposedSemaphoreFailures()
    {
        using var fixture = new CacheFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new DelegateSource(async (id, language, _) =>
        {
            started.TrySetResult();
            await release.Task; // A slow transport can finish after shutdown cancellation.
            return DelegateSource.Found(id, language);
        });
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions with { MaxConcurrency = 1 });
        var active = service.EnrichAsync([10], mode: CatalogNetworkMode.Online);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var sameId = service.EnrichAsync([10], mode: CatalogNetworkMode.Online);
        var differentId = service.EnrichAsync([20], mode: CatalogNetworkMode.Online);

        service.Dispose();
        fixture.Cache.Dispose();
        release.TrySetResult();

        foreach (var operation in new[] { active, sameId, differentId })
            await FluentActions.Awaiting(() => operation).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => service.EnrichAsync([30])).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task EnrichAsync_ShouldRejectMismatchedMetadataAndLimitOversizedInput()
    {
        using var fixture = new CacheFixture();
        var source = new DelegateSource((_, language, _) => Task.FromResult(DelegateSource.Found(999, language)));
        using var service = new SteamCatalogService(fixture.Cache, source, FastOptions with { MaxAppsPerRequest = 2 });
        var result = (await service.EnrichAsync([10], mode: CatalogNetworkMode.Online))[10];
        result.ErrorCode.Should().Be("invalid_metadata_identity");
        (await fixture.Cache.ReadAsync(10, "english")).Should().BeNull();
        await FluentActions.Awaiting(() => service.EnrichAsync([1, 2, 3])).Should().ThrowAsync<ArgumentException>();
    }
}
