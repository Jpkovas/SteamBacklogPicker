using FluentAssertions;
using SteamKit2.Internal;
using Xunit;

namespace SteamCatalog.Tests;

public sealed class FamilySessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetFamilySnapshotAsync_ShouldKeepLatestAuthoritativeRightsWhenCacheWriteFails(bool empty)
    {
        using var fixture = new CacheFixture();
        var time = new ManualTimeProvider();
        await fixture.Cache.WriteFamilyAsync(Snapshot(11, time, [new FamilyApp { AppId = 10, Access = FamilyAppAccess.Shared }]));
        var transport = new FakeFamilyTransport { CurrentIdentity = new SteamSessionIdentity(11, "fixture") };
        transport.Fetch = (id, language, _) => Task.FromResult(Snapshot(id, time,
            empty ? [] : [new FamilyApp { AppId = 20, Access = FamilyAppAccess.Shared }]) with { Language = language });
        var service = new SteamFamilySessionService(transport, new FailingFamilyWriteCache(fixture.Cache), time);

        await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);
        var offline = await service.GetFamilySnapshotAsync(11);
        transport.Fetch = (id, language, _) => Task.FromResult(new FamilySnapshot
        {
            SteamId = id, Language = language, Status = FamilySnapshotStatus.Error, ErrorCode = "network_failure"
        });
        var failure = await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);

        offline.Apps.Select(app => app.AppId).Should().BeEquivalentTo(empty ? Array.Empty<uint>() : [20u]);
        failure.Apps.Select(app => app.AppId).Should().BeEquivalentTo(offline.Apps.Select(app => app.AppId));
        failure.IsStale.Should().BeTrue();
        (await fixture.Cache.ReadFamilyAsync(11, "english"))!.Apps.Single().AppId.Should().Be(10,
            "the read-only cache remains old, so it must not override the newer in-memory result");
        await service.LogoutAsync();
        (await service.GetFamilySnapshotAsync(11)).Apps.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFamilySnapshotAsync_ShouldNotReuseUnpurgedRightsAfterRevocationAndFailedRevalidation()
    {
        using var fixture = new CacheFixture();
        var time = new ManualTimeProvider();
        await fixture.Cache.WriteFamilyAsync(Snapshot(11, time, [new FamilyApp { AppId = 10 }]));
        var transport = new FakeFamilyTransport { CurrentIdentity = new SteamSessionIdentity(11, "fixture") };
        var service = new SteamFamilySessionService(transport, new FailingFamilyWriteCache(fixture.Cache) { FailDelete = true }, time);
        transport.Fetch = (id, language, _) => Task.FromResult(new FamilySnapshot
        {
            SteamId = id, Language = language, Status = FamilySnapshotStatus.Revoked
        });
        await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);
        await service.LoginWithQrAsync();
        transport.Fetch = (id, language, _) => Task.FromResult(new FamilySnapshot
        {
            SteamId = id, Language = language, Status = FamilySnapshotStatus.Error, ErrorCode = "unavailable"
        });

        var result = await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);

        result.Status.Should().Be(FamilySnapshotStatus.Error);
        result.Apps.Should().BeEmpty();
        (await fixture.Cache.ReadFamilyAsync(11, "english"))!.Apps.Should().ContainSingle();
    }

    [Fact]
    public async Task SteamKitConnection_ShouldAllowStopAfterItsAsynchronousCleanupFinished()
    {
        // The host can retain a connection reference while an operation resets/disposes it.
        // Exercise that lifetime without connecting or requiring a real Steam account.
        var type = typeof(SteamKitCatalogTransport).GetNestedType("Connection", System.Reflection.BindingFlags.NonPublic)!;
        var connection = (IAsyncDisposable)Activator.CreateInstance(type, nonPublic: true)!;
        await connection.DisposeAsync();

        Action stop = () => type.GetMethod("RequestStop")!.Invoke(connection, null);

        stop.Should().NotThrow();
    }

    [Fact]
    public async Task GetFamilySnapshotAsync_ShouldKeepValidEmptySeparateFromFailureAndReplaceOldRights()
    {
        using var fixture = new CacheFixture();
        var time = new ManualTimeProvider();
        var transport = new FakeFamilyTransport { CurrentIdentity = new SteamSessionIdentity(11, "fixture") };
        var service = new SteamFamilySessionService(transport, fixture.Cache, time);
        await fixture.Cache.WriteFamilyAsync(Snapshot(11, time, [new FamilyApp { AppId = 10, Access = FamilyAppAccess.Shared }]));
        transport.Fetch = (id, language, _) => Task.FromResult(new FamilySnapshot
        {
            SteamId = id, Language = language, Status = FamilySnapshotStatus.Error, ErrorCode = "server_unavailable"
        });

        var failed = await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);
        failed.Status.Should().Be(FamilySnapshotStatus.Error);
        failed.Apps.Should().ContainSingle();
        failed.IsStale.Should().BeTrue();

        transport.Fetch = (id, language, _) => Task.FromResult(new FamilySnapshot
        {
            SteamId = id, Language = language, Status = FamilySnapshotStatus.Success, HasFamily = true, FamilyGroupId = 123
        });
        var empty = await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);
        empty.Status.Should().Be(FamilySnapshotStatus.Success);
        empty.HasFamily.Should().BeTrue();
        empty.Apps.Should().BeEmpty();
        (await fixture.Cache.ReadFamilyAsync(11, "english"))!.Apps.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFamilySnapshotAsync_ShouldKeepNoGroupDistinctAndNeverInventOwnedGames()
    {
        using var fixture = new CacheFixture();
        var transport = new FakeFamilyTransport { CurrentIdentity = new SteamSessionIdentity(11, "fixture") };
        var service = new SteamFamilySessionService(transport, fixture.Cache);

        var result = await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);

        result.Status.Should().Be(FamilySnapshotStatus.Success);
        result.HasFamily.Should().BeFalse();
        result.FamilyGroupId.Should().BeNull();
        result.Apps.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFamilySnapshotAsync_ShouldRequireMatchingExplicitOnlineIdentity()
    {
        using var fixture = new CacheFixture();
        var transport = new FakeFamilyTransport { CurrentIdentity = new SteamSessionIdentity(22, "other") };
        var service = new SteamFamilySessionService(transport, fixture.Cache);
        await fixture.Cache.WriteFamilyAsync(Snapshot(11, new ManualTimeProvider(), [new FamilyApp { AppId = 10 }]));

        var mismatch = await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);
        var anotherOfflineIdentity = await service.GetFamilySnapshotAsync(22);

        mismatch.Status.Should().Be(FamilySnapshotStatus.NotAuthenticated);
        mismatch.Apps.Should().BeEmpty();
        anotherOfflineIdentity.Apps.Should().BeEmpty();
        transport.FetchCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetFamilySnapshotAsync_ShouldPurgeAllLanguagesOnRevocationAndRejectOfflineReuse()
    {
        using var fixture = new CacheFixture();
        var time = new ManualTimeProvider();
        var transport = new FakeFamilyTransport { CurrentIdentity = new SteamSessionIdentity(11, "fixture") };
        var service = new SteamFamilySessionService(transport, fixture.Cache, time);
        await fixture.Cache.WriteFamilyAsync(Snapshot(11, time, [new FamilyApp { AppId = 10 }]));
        await fixture.Cache.WriteFamilyAsync(Snapshot(11, time, [new FamilyApp { AppId = 10 }]) with { Language = "brazilian" });
        transport.Fetch = (id, language, _) => Task.FromResult(new FamilySnapshot
        {
            SteamId = id, Language = language, Status = FamilySnapshotStatus.Revoked, ErrorCode = "access_revoked"
        });

        var revoked = await service.GetFamilySnapshotAsync(11, mode: CatalogNetworkMode.Online);
        revoked.Status.Should().Be(FamilySnapshotStatus.Revoked);
        revoked.Apps.Should().BeEmpty();
        (await fixture.Cache.ReadFamilyAsync(11, "english")).Should().BeNull();
        (await fixture.Cache.ReadFamilyAsync(11, "brazilian")).Should().BeNull();
        (await service.GetFamilySnapshotAsync(11)).Status.Should().Be(FamilySnapshotStatus.NotAuthenticated);
    }

    [Fact]
    public async Task GetFamilySnapshotAsync_ShouldMarkExpiredOfflineEvidenceWithoutNetwork()
    {
        using var fixture = new CacheFixture();
        var time = new ManualTimeProvider();
        var transport = new FakeFamilyTransport();
        var service = new SteamFamilySessionService(transport, fixture.Cache, time);
        await fixture.Cache.WriteFamilyAsync(Snapshot(11, time, [new FamilyApp { AppId = 10 }]));
        time.Advance(TimeSpan.FromHours(1));

        var result = await service.GetFamilySnapshotAsync(11);

        result.Status.Should().Be(FamilySnapshotStatus.Success);
        result.IsFromCache.Should().BeTrue();
        result.IsStale.Should().BeTrue();
        transport.FetchCalls.Should().Be(0);
    }

    [Fact]
    public async Task LogoutAsync_ShouldClearIdentityAndCachedRights()
    {
        using var fixture = new CacheFixture();
        var transport = new FakeFamilyTransport { CurrentIdentity = new SteamSessionIdentity(11, "fixture") };
        var service = new SteamFamilySessionService(transport, fixture.Cache);
        await fixture.Cache.WriteFamilyAsync(Snapshot(11, new ManualTimeProvider(), [new FamilyApp { AppId = 10 }]));

        await service.LogoutAsync();

        service.CurrentIdentity.Should().BeNull();
        (await fixture.Cache.ReadFamilyAsync(11, "english")).Should().BeNull();
    }

    [Fact]
    public async Task LoginWithQrAsync_ShouldForwardChallengeAndCancellationWithoutPersistingIt()
    {
        using var fixture = new CacheFixture();
        using var cancellation = new CancellationTokenSource();
        var challengeReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeFamilyTransport
        {
            Login = async (progress, token) =>
            {
                progress?.Report(new SteamLoginProgress(SteamLoginStage.AwaitingQrApproval, "https://s.team/q/fixture"));
                challengeReported.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new SteamSessionIdentity(11, "unreachable");
            }
        };
        var service = new SteamFamilySessionService(transport, fixture.Cache);
        var updates = new List<SteamLoginProgress>();
        var login = service.LoginWithQrAsync(new ImmediateProgress<SteamLoginProgress>(updates.Add), cancellation.Token);
        await challengeReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await FluentActions.Awaiting(() => login).Should().ThrowAsync<OperationCanceledException>();
        updates.Should().ContainSingle().Which.ChallengeUrl.Should().Be("https://s.team/q/fixture");
        updates[0].ToString().Should().NotContain("s.team");
        service.CurrentIdentity.Should().BeNull();
        (await fixture.Cache.ReadFamilyAsync(11, "english")).Should().BeNull();
    }

    [Fact]
    public void MapFamilyApps_ShouldPreserveOwnerIdsExclusionsAndUnknownAvailability()
    {
        var response = new CFamilyGroups_GetSharedLibraryApps_Response();
        response.apps.Add(new() { appid = 10, name = "Shared", owner_steamids = { 22, 22 }, exclude_reason = ESharedLibraryExcludeReason.k_ESharedLibrary_Included });
        response.apps.Add(new() { appid = 20, name = "Private", owner_steamids = { 22 }, exclude_reason = ESharedLibraryExcludeReason.k_ESharedLibrary_LicensePrivate });
        response.apps.Add(new() { appid = 30, name = "Own", owner_steamids = { 11, 22 }, exclude_reason = ESharedLibraryExcludeReason.k_ESharedLibrary_AppExcluded_ByPartner });
        response.apps.Add(new() { appid = 40, name = "Unknown" });

        var snapshot = SteamKitCatalogTransport.MapFamilyApps(11, "english", 123, response);

        snapshot.Apps.Select(app => app.Access).Should().Equal(FamilyAppAccess.Shared, FamilyAppAccess.Excluded, FamilyAppAccess.Owned, FamilyAppAccess.Unknown);
        snapshot.Apps[0].OwnerSteamIds.Should().Equal(22);
        snapshot.Apps[1].ExcludeReason.Should().Be((int)ESharedLibraryExcludeReason.k_ESharedLibrary_LicensePrivate);
        snapshot.Apps.Should().OnlyContain(app => !app.IsCurrentAvailabilityKnown);
    }

    [Fact]
    public async Task MemorySteamTokenStore_ShouldKeepTokensOnlyInMemoryAndRedactDiagnostics()
    {
        var store = new MemorySteamTokenStore();
        var credential = new SteamRefreshCredential("fixture", "fixture-not-a-real-token");
        await store.WriteAsync(11, credential);
        store.IsPersistent.Should().BeFalse();
        credential.ToString().Should().NotContain("fixture");
        (await store.ReadAsync(22)).Should().BeNull();
        (await store.ReadAsync(11)).Should().BeSameAs(credential);
        await store.DeleteAsync(11);
        (await store.ReadAsync(11)).Should().BeNull();
    }

    [Fact]
    public async Task SteamKitCatalogTransport_ShouldConstructOfflineAndSupportBothShutdownContracts()
    {
        var transport = new SteamKitCatalogTransport();
        transport.CurrentIdentity.Should().BeNull();
        ((IDisposable)transport).Dispose();
        await transport.DisposeAsync();
    }

    private static FamilySnapshot Snapshot(ulong id, ManualTimeProvider time, IReadOnlyList<FamilyApp> apps) => new()
    {
        SteamId = id, Status = FamilySnapshotStatus.Success, HasFamily = true, FamilyGroupId = 123,
        Apps = apps, FetchedAt = time.GetUtcNow(), ExpiresAt = time.GetUtcNow().AddMinutes(30)
    };

    private sealed class FailingFamilyWriteCache(ICatalogCache inner) : ICatalogCache
    {
        public bool FailDelete { get; init; }
        public Task<CachedCatalogEntry?> ReadAsync(uint id, string language, CancellationToken token = default) => inner.ReadAsync(id, language, token);
        public Task WriteAsync(CachedCatalogEntry entry, CancellationToken token = default) => inner.WriteAsync(entry, token);
        public Task<FamilySnapshot?> ReadFamilyAsync(ulong id, string language, CancellationToken token = default) => inner.ReadFamilyAsync(id, language, token);
        public Task WriteFamilyAsync(FamilySnapshot snapshot, CancellationToken token = default)
            => Task.FromException(new IOException("Fixture cannot write Family evidence."));
        public Task DeleteFamilyAsync(ulong id, CancellationToken token = default)
            => FailDelete ? Task.FromException(new IOException("Fixture cannot purge Family evidence.")) : inner.DeleteFamilyAsync(id, token);
    }
}
