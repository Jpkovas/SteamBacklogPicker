using FluentAssertions;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace SteamCatalog.Tests;

public sealed class SqliteCacheTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EnrichAsync_ShouldReadTwoThousandCachedAppsWithoutNetwork()
    {
        using var fixture = new CacheFixture();
        var ids = Enumerable.Range(1, 2000).Select(id => (uint)id).ToArray();
        var now = DateTimeOffset.UtcNow;
        foreach (var id in ids)
            await fixture.Cache.WriteAsync(new(id, "english", CatalogLookupStatus.Found,
                DelegateSource.Found(id, "english").Item, now, now.AddDays(1)));
        var source = new DelegateSource((_, _, _) => throw new InvalidOperationException("Offline must not call the network."));
        using var service = new SteamCatalogService(fixture.Cache, source);
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();

        var results = await service.EnrichAsync(ids);

        elapsed.Stop();
        output.WriteLine($"sqlite-offline-2000: elapsedMs={elapsed.Elapsed.TotalMilliseconds:F1}; allocatedBytes={GC.GetTotalAllocatedBytes(precise: true) - before}; results={results.Count}; networkCalls={source.Calls}");
        results.Should().HaveCount(2000);
        results.Values.Should().OnlyContain(result => result.IsFromCache && result.Status == CatalogLookupStatus.Found);
        source.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_ShouldCancelOperationsWaitingForAnExternalDatabaseLock(bool bulk)
    {
        using var fixture = new CacheFixture();
        await fixture.Cache.ReadAsync(10, "english");
        await using var blocker = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Cache.DatabasePath, Pooling = false
        }.ToString());
        await blocker.OpenAsync();
        using var command = blocker.CreateCommand();
        command.CommandText = "BEGIN EXCLUSIVE";
        await command.ExecuteNonQueryAsync();
        Task Read(uint id) => bulk ? fixture.Cache.ReadManyAsync([id], "english") : fixture.Cache.ReadAsync(id, "english");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = Task.Run(async () =>
        {
            started.TrySetResult();
            await Read(10);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The real SQLite lock keeps the connection operation in flight while disposal occurs.
        await FluentActions.Awaiting(() => active.WaitAsync(TimeSpan.FromMilliseconds(150)))
            .Should().ThrowAsync<TimeoutException>();
        var queued = Read(20);

        fixture.Cache.Dispose();
        command.CommandText = "ROLLBACK";
        await command.ExecuteNonQueryAsync();

        await FluentActions.Awaiting(() => active.WaitAsync(TimeSpan.FromSeconds(5))).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => queued).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => Read(30)).Should().ThrowAsync<ObjectDisposedException>();
    }
}
