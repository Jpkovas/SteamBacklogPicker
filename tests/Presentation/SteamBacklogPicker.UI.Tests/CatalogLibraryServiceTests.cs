using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Library;
using SteamCatalog;
using SteamClientAdapter;
using Xunit;
using Xunit.Abstractions;

namespace SteamBacklogPicker.UI.Tests;

public sealed class CatalogLibraryServiceTests(ITestOutputHelper output)
{
    private const ulong AccountA = 76561198000000001;
    private const ulong AccountB = 76561198000000002;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task FailedDiscoveryAfterAccountSwitch_ShouldClearPreviousAccountLibrary()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        await fixture.RefreshAsync();
        fixture.Latest.Games.Should().NotBeEmpty();
        fixture.Profile.Account = AccountB.ToString();
        fixture.Local.Load = _ => throw new IOException("Fixture library unavailable");

        Func<Task> refresh = () => fixture.Service.GetLibraryAsync();
        await refresh.Should().ThrowAsync<IOException>();

        fixture.Latest.AccountId.Should().Be(AccountB.ToString());
        fixture.Latest.Games.Should().BeEmpty();
        fixture.Service.Status.AccountId.Should().Be(AccountB.ToString());
    }

    [Fact]
    public async Task LocalSnapshot_ShouldSkipNullIdentifiersAndNormalizeNullableFields()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Local.Games = [];
        var games = new object?[] { null, new { Id = (object?)null }, new
            { Id = GameIdentifier.ForSteam(10), Title = (string?)null, Tags = (object?)null,
                StoreCategoryIds = (object?)null, SupportedPlatforms = (object?)null } };
        var json = JsonSerializer.Serialize(new { Version = 1, AccountId = AccountA.ToString(), Source = "local-discovery", Games = games });
        await File.WriteAllTextAsync(fixture.SnapshotPath(AccountA), json);

        var restored = await fixture.RefreshAsync();

        var entry = restored.Should().ContainSingle().Subject;
        entry.SteamAppId.Should().Be(10);
        entry.Title.Should().Be("App 10");
        entry.Tags.Should().BeEmpty();
        entry.SupportedPlatforms.Should().BeEmpty();
        entry.InstallState.Should().Be(InstallState.Unknown);
    }

    [Fact]
    public async Task StaleFamilyResponse_ShouldRetainFreshLocalOwnershipEvidence()
    {
        using var fixture = new Fixture();
        fixture.Local.Games = [Game(10, "Owned locally")];
        fixture.Family.Snapshot = (id, language, _, _) => Task.FromResult(Family(id, language, [Shared(10), Shared(20)]) with
            { IsStale = true, IsFromCache = true });

        await fixture.RefreshAsync();

        fixture.Latest.Games.Single(game => game.SteamAppId == 10).OwnershipType.Should().Be(OwnershipType.Owned);
        fixture.Latest.Games.Single(game => game.SteamAppId == 20).OwnershipType.Should().Be(OwnershipType.Unknown);
    }

    [Fact]
    public async Task RefreshAsync_ShouldAvoidFloodingObserversForAnUnchangedTwoThousandGameCatalog()
    {
        using var fixture = new Fixture();
        fixture.Local.Games = Enumerable.Range(1, 2000).Select(id => Game((uint)id, "Game " + id)).ToArray();
        fixture.Catalog.Fetch = (ids, language, _, _) => Task.FromResult<IReadOnlyDictionary<uint, CatalogLookupResult>>(
            ids.ToDictionary(id => id, id => new CatalogLookupResult(id, CatalogLookupStatus.Found,
                new CatalogItem { AppId = id, Name = "Game " + id, Language = language, ProductType = "game" })));
        var statusEvents = 0;
        fixture.Service.StatusChanged += (_, _) => Interlocked.Increment(ref statusEvents);
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();

        await fixture.RefreshAsync();

        elapsed.Stop();
        output.WriteLine($"unchanged-2000: snapshots={fixture.Snapshots.Count}; statuses={statusEvents}; elapsedMs={elapsed.Elapsed.TotalMilliseconds:F1}; allocatedBytes={GC.GetTotalAllocatedBytes(precise: true) - before}");
        fixture.Latest.Games.Should().HaveCount(2000);
        fixture.Snapshots.Count.Should().BeLessThanOrEqualTo(3);
        statusEvents.Should().BeLessThanOrEqualTo(8);
    }

    [Fact]
    public async Task RefreshAsync_ShouldCoalesceFastBatchesAndKeepPublishedSnapshotsImmutable()
    {
        var time = new TimestampTimeProvider();
        using var fixture = new Fixture(time);
        fixture.Local.Games = Enumerable.Range(1, 100).Select(id => Game((uint)id, "Original " + id)).ToArray();
        fixture.Catalog.Fetch = (ids, language, mode, _) =>
        {
            if (mode == CatalogNetworkMode.Offline) return Task.FromResult(EmptyMetadata());
            time.Advance(TimeSpan.FromMilliseconds(100));
            return Task.FromResult<IReadOnlyDictionary<uint, CatalogLookupResult>>(ids.ToDictionary(id => id,
                id => new CatalogLookupResult(id, CatalogLookupStatus.Found,
                    new CatalogItem { AppId = id, Name = "Updated " + id, Language = language, ProductType = "game" })));
        };

        await fixture.RefreshAsync();

        fixture.Snapshots.Should().HaveCount(3, "initial, one update at 300 ms, and the final trailing update are sufficient");
        var snapshots = fixture.Snapshots.ToArray();
        snapshots[0].Games.Should().OnlyContain(game => game.Title.StartsWith("Original "));
        snapshots[1].Games.Take(60).Should().OnlyContain(game => game.Title.StartsWith("Updated "));
        snapshots[1].Games.Skip(60).Should().OnlyContain(game => game.Title.StartsWith("Original "));
        snapshots[2].Games.Should().OnlyContain(game => game.Title.StartsWith("Updated "));
        fixture.Service.Status.MissingNames.Should().Be(0);
        (await File.ReadAllTextAsync(fixture.SnapshotPath(AccountA))).Should().Contain("Updated 100");
    }

    [Fact]
    public async Task CancelSynchronization_ShouldDiscardPendingCoalescedChanges()
    {
        using var fixture = new Fixture(new TimestampTimeProvider());
        fixture.Local.Games = Enumerable.Range(1, 40).Select(id => Game((uint)id, "Original " + id)).ToArray();
        var secondBatch = Signal();
        var cancellationObserved = Signal();
        var batchCount = 0;
        fixture.Catalog.Fetch = async (ids, language, mode, token) =>
        {
            if (mode == CatalogNetworkMode.Offline) return EmptyMetadata();
            if (++batchCount == 2)
            {
                secondBatch.TrySetResult();
                try { await Task.Delay(System.Threading.Timeout.Infinite, token); }
                finally { cancellationObserved.TrySetResult(); }
            }
            return ids.ToDictionary(id => id, id => new CatalogLookupResult(id, CatalogLookupStatus.Found,
                new CatalogItem { AppId = id, Name = "Pending " + id, Language = language, ProductType = "game" }));
        };
        await fixture.Service.GetLibraryAsync();
        await secondBatch.Task.WaitAsync(Timeout);

        fixture.Service.CancelSynchronization();
        await cancellationObserved.Task.WaitAsync(Timeout);

        fixture.Snapshots.Should().ContainSingle();
        fixture.Latest.Games.Should().OnlyContain(game => game.Title.StartsWith("Original "));
        fixture.Service.Status.IsBusy.Should().BeFalse();
        (await File.ReadAllTextAsync(fixture.SnapshotPath(AccountA))).Should().NotContain("Pending ");
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldPublishLocalBeforeOnlineAndRefreshKnownTitles()
    {
        using var fixture = new Fixture();
        fixture.Local.Games = [Game(10, "Known title")];
        var started = Signal();
        var release = Signal();
        fixture.Catalog.Fetch = async (ids, language, mode, token) =>
        {
            if (mode == CatalogNetworkMode.Offline) return EmptyMetadata();
            ids.Should().Equal(10);
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return Metadata(10, language, "Título atualizado");
        };
        using var idle = new IdleWaiter(fixture.Service);

        var local = await fixture.Service.GetLibraryAsync().WaitAsync(Timeout);
        local.Single().Title.Should().Be("Known title");
        await started.Task.WaitAsync(Timeout);
        idle.Task.IsCompleted.Should().BeFalse();
        release.TrySetResult();
        await idle.Task.WaitAsync(Timeout);

        fixture.Latest.Games.Single().Title.Should().Be("Título atualizado");
        fixture.Catalog.Calls.Should().Contain(call => call.Mode == CatalogNetworkMode.Online && call.Ids.Contains(10u));
    }

    [Fact]
    public async Task LanguageChange_ShouldDiscardLateMetadataAndPersistOnlyCurrentLanguage()
    {
        using var fixture = new Fixture();
        var started = Signal();
        var release = new TaskCompletionSource<IReadOnlyDictionary<uint, CatalogLookupResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = Signal();
        fixture.Catalog.Fetch = async (_, language, mode, _) =>
        {
            if (mode == CatalogNetworkMode.Offline) return EmptyMetadata();
            if (language == "brazilian")
            {
                started.TrySetResult();
                var response = await release.Task; // Deliberately emulate a source that ignores cancellation.
                returned.TrySetResult();
                return response;
            }
            return Metadata(10, language, "Current English title");
        };
        await fixture.Service.GetLibraryAsync();
        await started.Task.WaitAsync(Timeout);
        var oldGeneration = fixture.Service.Status.Generation;
        fixture.Service.Language = "en-US";
        await fixture.RefreshAsync();
        var newGeneration = fixture.Service.Status.Generation;
        var count = fixture.Snapshots.Count;

        release.TrySetResult(Metadata(10, "brazilian", "Obsolete title"));
        await returned.Task.WaitAsync(Timeout);
        // A following refresh also joins all observable persistence work through the current generation.
        await fixture.RefreshAsync();

        fixture.Snapshots.Skip(count).Should().OnlyContain(snapshot => snapshot.Generation > oldGeneration);
        fixture.Latest.Generation.Should().BeGreaterThanOrEqualTo(newGeneration);
        fixture.Latest.Games.Single().Title.Should().Be("Current English title");
        var stored = await File.ReadAllTextAsync(fixture.SnapshotPath(AccountA));
        stored.Should().Contain("Current English title").And.NotContain("Obsolete title");
        fixture.Catalog.Calls.Should().Contain(call => call.Language == "english" && call.Mode == CatalogNetworkMode.Online);
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldRetryWhenLocalIdentityChangesDuringDiscovery()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        var calls = 0;
        fixture.Local.Load = _ =>
        {
            if (++calls == 1)
            {
                fixture.Profile.Account = AccountB.ToString();
                return Task.FromResult<IReadOnlyList<GameEntry>>([Game(11, "Previous account")]);
            }
            return Task.FromResult<IReadOnlyList<GameEntry>>([Game(22, "Current account")]);
        };

        var games = await fixture.RefreshAsync();

        calls.Should().Be(2);
        games.Select(game => game.SteamAppId).Should().Equal(22u);
        fixture.Latest.AccountId.Should().Be(AccountB.ToString());
        File.Exists(fixture.SnapshotPath(AccountA)).Should().BeFalse();
        (await File.ReadAllTextAsync(fixture.SnapshotPath(AccountB))).Should().NotContain("Previous account");
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldCancelInsteadOfReturningAmbiguousIdentity()
    {
        using var fixture = new Fixture();
        fixture.Local.Load = _ =>
        {
            fixture.Profile.Account = fixture.Profile.Account == AccountA.ToString() ? AccountB.ToString() : AccountA.ToString();
            return Task.FromResult<IReadOnlyList<GameEntry>>([Game(11)]);
        };

        Func<Task> action = () => fixture.Service.GetLibraryAsync();

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Latest.Games.Should().BeEmpty();
        fixture.Service.Status.Error.Should().Be("steam_profile_changed_during_refresh");
        fixture.Service.Status.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldRejectIdentityChangeDuringCacheLookup()
    {
        using var fixture = new Fixture();
        fixture.Catalog.Fetch = (_, _, _, _) =>
        {
            fixture.Profile.Account = AccountB.ToString();
            return Task.FromResult(EmptyMetadata());
        };

        Func<Task> action = () => fixture.Service.GetLibraryAsync();

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Latest.AccountId.Should().Be(AccountB.ToString());
        fixture.Latest.Games.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldPreserveOfflineLocalSnapshotWithoutClaimingCurrentRights()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Local.Games = [Game(10, "Saved local title")];
        await fixture.RefreshAsync();
        fixture.Local.Games = [];

        var games = await fixture.RefreshAsync();

        games.Single().Title.Should().Be("Saved local title");
        games.Single().OwnershipType.Should().Be(OwnershipType.Unknown);
        games.Single().InstallState.Should().Be(InstallState.Unknown);
        fixture.Catalog.Calls.Should().OnlyContain(call => call.Mode == CatalogNetworkMode.Offline);
        fixture.Family.Calls.Should().OnlyContain(call => call.Mode == CatalogNetworkMode.Offline);
    }

    [Fact]
    public async Task DisconnectAsync_ShouldNotReplayFamilyGamesFromLocalSnapshot()
    {
        using var fixture = new Fixture();
        fixture.Family.Identity = new(AccountA, "test-account");
        fixture.Family.Snapshot = (id, language, _, _) => Task.FromResult(Family(id, language, [Shared(20)]));
        await fixture.RefreshAsync();
        fixture.Latest.Games.Select(game => game.SteamAppId).Should().BeEquivalentTo([10u, 20u]);
        var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SnapshotPath(AccountA)));
        document.RootElement.GetProperty("Games").GetArrayLength().Should().Be(1);
        document.RootElement.GetProperty("Source").GetString().Should().Be("local-discovery");

        await fixture.Service.DisconnectAsync();
        fixture.Latest.Games.Should().BeEmpty();
        fixture.Local.Games = [];
        fixture.Service.NetworkEnabled = false;
        fixture.Family.Snapshot = null;
        await fixture.RefreshAsync();

        fixture.Latest.Games.Select(game => game.SteamAppId).Should().Equal(10u);
        fixture.Latest.Games.Single().OwnershipType.Should().Be(OwnershipType.Unknown);
    }

    [Theory]
    [InlineData(FamilySnapshotStatus.Revoked)]
    [InlineData(FamilySnapshotStatus.NotAuthenticated)]
    public async Task GetLibraryAsync_ShouldIgnoreFamilyAppsOnNegativeAuthorizationEvenWithCacheFlag(FamilySnapshotStatus status)
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Family.Snapshot = (id, language, _, _) => Task.FromResult(Family(id, language, [Shared(20)]) with
        {
            Status = status, IsFromCache = true, IsStale = true
        });

        await fixture.RefreshAsync();

        fixture.Latest.Games.Select(game => game.SteamAppId).Should().Equal(10u);
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldRestoreOnlyFamilyRightsIncludedInAuthoritativeSnapshot()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Local.Games =
        [
            Game(10) with { OwnershipType = OwnershipType.FamilyShared },
            Game(11) with { OwnershipType = OwnershipType.FamilyShared },
            Game(12)
        ];
        fixture.Family.Snapshot = (id, language, _, _) => Task.FromResult(Family(id, language,
        [
            Shared(10), new FamilyApp { AppId = 12, Access = FamilyAppAccess.Owned, ExcludeReason = 3 }
        ]));

        await fixture.RefreshAsync();

        fixture.Latest.Games.Single(game => game.SteamAppId == 10).OwnershipType.Should().Be(OwnershipType.FamilyShared);
        fixture.Latest.Games.Single(game => game.SteamAppId == 11).OwnershipType.Should().Be(OwnershipType.Unknown);
        fixture.Latest.Games.Single(game => game.SteamAppId == 12).OwnershipType.Should().Be(OwnershipType.Owned);
        fixture.Latest.Games.Should().OnlyContain(game => game.InstallState == InstallState.Installed);
    }

    [Theory]
    [InlineData(FamilySnapshotStatus.Success, true, false)]
    [InlineData(FamilySnapshotStatus.Success, false, false)]
    [InlineData(FamilySnapshotStatus.Success, true, true)]
    [InlineData(FamilySnapshotStatus.Revoked, true, false)]
    [InlineData(FamilySnapshotStatus.NotAuthenticated, true, false)]
    public async Task GetLibraryAsync_ShouldClearOldLocalSharingFlagsWhenFamilyEvidenceDoesNotConfirmAccess(
        FamilySnapshotStatus status, bool hasFamily, bool excluded)
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Local.Games = [Game(10) with { OwnershipType = OwnershipType.FamilyShared }, Game(11)];
        fixture.Family.Snapshot = (id, language, _, _) => Task.FromResult(Family(id, language,
            excluded ? [Shared(10) with { Access = FamilyAppAccess.Excluded }] : []) with
        {
            Status = status, HasFamily = hasFamily
        });

        await fixture.RefreshAsync();

        fixture.Latest.Games.Single(game => game.SteamAppId == 10).OwnershipType.Should().Be(OwnershipType.Unknown);
        fixture.Latest.Games.Single(game => game.SteamAppId == 11).OwnershipType.Should().Be(OwnershipType.Owned);
        fixture.Latest.Games.Should().OnlyContain(game => game.InstallState == InstallState.Installed);
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldRejectFamilyResponseForAnotherIdentity()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Family.Snapshot = (_, language, _, _) => Task.FromResult(Family(AccountB, language, [Shared(20)]));

        await fixture.RefreshAsync();

        fixture.Latest.Games.Select(game => game.SteamAppId).Should().Equal(10u);
        fixture.Service.Status.Error.Should().Be("family_response_identity_mismatch");
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldPreserveTransientFamilyCacheAsUnknownThenHonorValidEmptyResponse()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Family.Snapshot = (id, language, _, _) => Task.FromResult(Family(id, language, [Shared(20)]) with
        {
            Status = FamilySnapshotStatus.Error, IsFromCache = true, IsStale = true, ErrorCode = "offline"
        });
        await fixture.RefreshAsync();
        fixture.Latest.Games.Single(game => game.SteamAppId == 20).OwnershipType.Should().Be(OwnershipType.Unknown);
        fixture.Service.Status.Error.Should().Be("offline");

        fixture.Family.Snapshot = (id, language, _, _) => Task.FromResult(Family(id, language, []));
        await fixture.RefreshAsync();

        fixture.Latest.Games.Select(game => game.SteamAppId).Should().Equal(10u);
        fixture.Service.Status.Error.Should().BeNull();
    }

    [Fact]
    public async Task GetLibraryAsync_ShouldKeepOnlyInstalledLocalEvidenceWhenAuthenticatedAccountDiffers()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Local.Games = [Game(10) with { Tags = ["Private collection"] }, Game(11) with { InstallState = InstallState.Available }];
        fixture.Family.Identity = new(AccountB, "other-account");

        var games = await fixture.RefreshAsync();

        games.Select(game => game.SteamAppId).Should().Equal(10u);
        games.Single().OwnershipType.Should().Be(OwnershipType.Unknown);
        games.Single().Tags.Should().BeEmpty();
        fixture.Latest.AccountId.Should().Be(AccountB.ToString());
    }

    [Fact]
    public async Task CancelSynchronization_ShouldCancelQrAndSuppressLateChallengeAndLogin()
    {
        using var fixture = new Fixture();
        var started = Signal();
        var release = Signal();
        CancellationToken loginToken = default;
        fixture.Family.Login = async (progress, token) =>
        {
            loginToken = token;
            progress!.Report(new(SteamLoginStage.AwaitingQrApproval, "https://s.team/q/test-ephemeral"));
            started.TrySetResult();
            await release.Task;
            progress.Report(new(SteamLoginStage.AwaitingQrApproval, "https://s.team/q/late-ephemeral"));
            return new(AccountA, "test-account");
        };
        var connection = fixture.Service.ConnectAsync();
        await started.Task.WaitAsync(Timeout);
        fixture.Service.Status.QrChallengeUrl.Should().NotBeNull();

        fixture.Service.CancelSynchronization();
        var generation = fixture.Service.Status.Generation;
        loginToken.IsCancellationRequested.Should().BeTrue();
        release.TrySetResult();
        Func<Task> action = () => connection;
        await action.Should().ThrowAsync<OperationCanceledException>();

        fixture.Family.Identity.Should().BeNull();
        fixture.Service.Status.Generation.Should().Be(generation);
        fixture.Service.Status.QrChallengeUrl.Should().BeNull();
        fixture.Service.Status.IsBusy.Should().BeFalse();
        fixture.Service.Status.Message.Should().Contain("cancelada").And.NotContain("Aguardando");
        Directory.GetFiles(fixture.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task NetworkEnabled_ShouldPersistOnlyPreferenceAndRestoreOfflineMode()
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        using var restarted = new CatalogLibraryService(fixture.Local, fixture.Profile, fixture.Catalog, fixture.Family, fixture.Directory);
        restarted.NetworkEnabled.Should().BeFalse();
        using var idle = new IdleWaiter(restarted);

        await restarted.GetLibraryAsync();
        await idle.Task.WaitAsync(Timeout);

        fixture.Catalog.Calls.Should().OnlyContain(call => call.Mode == CatalogNetworkMode.Offline);
        var preference = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Directory, "synchronization-preferences.json")));
        preference.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("Version", "NetworkEnabled");
        preference.RootElement.GetProperty("NetworkEnabled").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetLibraryAsync_ShouldRejectUnattributedOrMismatchedSnapshot(bool legacyArray)
    {
        using var fixture = new Fixture();
        fixture.Service.NetworkEnabled = false;
        fixture.Local.Games = [];
        var json = legacyArray ? JsonSerializer.Serialize(new[] { Game(99) })
            : JsonSerializer.Serialize(new { Version = 1, AccountId = AccountB.ToString(), Source = "local-discovery", Games = new[] { Game(99) } });
        await File.WriteAllTextAsync(fixture.SnapshotPath(AccountA), json);

        var games = await fixture.RefreshAsync();

        games.Should().BeEmpty();
        fixture.Latest.Games.Should().BeEmpty();
    }

    private static GameEntry Game(uint id, string title = "Local title") => new()
    {
        Id = GameIdentifier.ForSteam(id), Title = title, OwnershipType = OwnershipType.Owned,
        InstallState = InstallState.Installed, ProductCategory = ProductCategory.Game
    };
    private static FamilyApp Shared(uint id) => new() { AppId = id, Name = "Family title " + id, ProductType = "game", Access = FamilyAppAccess.Shared, OwnerSteamIds = [AccountB] };
    private static FamilySnapshot Family(ulong id, string language, IReadOnlyList<FamilyApp> apps) => new()
    {
        SteamId = id, Language = language, Status = FamilySnapshotStatus.Success, HasFamily = true, Apps = apps
    };
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IReadOnlyDictionary<uint, CatalogLookupResult> EmptyMetadata() => new Dictionary<uint, CatalogLookupResult>();
    private static IReadOnlyDictionary<uint, CatalogLookupResult> Metadata(uint id, string language, string title)
        => new Dictionary<uint, CatalogLookupResult> { [id] = new(id, CatalogLookupStatus.Found, new CatalogItem { AppId = id, Language = language, Name = title, ProductType = "game" }) };

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "SteamBacklogPicker.CatalogCoordinator.Tests", Guid.NewGuid().ToString("N"));
        public LocalLibrary Local { get; } = new();
        public Profile Profile { get; } = new();
        public Catalog Catalog { get; } = new();
        public FamilySession Family { get; } = new();
        public CatalogLibraryService Service { get; }
        public ConcurrentQueue<LibrarySnapshotEventArgs> Snapshots { get; } = new();
        public LibrarySnapshotEventArgs Latest => Snapshots.Last();
        public Fixture(TimeProvider? timeProvider = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Service = new(Local, Profile, Catalog, Family, Directory, timeProvider);
            Service.SnapshotChanged += (_, snapshot) => Snapshots.Enqueue(snapshot);
        }
        public string SnapshotPath(ulong account) => Path.Combine(Directory, account + ".json");
        public async Task<IReadOnlyList<GameEntry>> RefreshAsync()
        {
            using var idle = new IdleWaiter(Service);
            var games = await Service.GetLibraryAsync().WaitAsync(Timeout);
            await idle.Task.WaitAsync(Timeout);
            return games;
        }
        public void Dispose()
        {
            Service.Dispose();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
    private sealed class TimestampTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
    }
    private sealed class IdleWaiter : IDisposable
    {
        private readonly CatalogLibraryService _service;
        private readonly TaskCompletionSource _completion = Signal();
        private bool _wasBusy;
        public Task Task => _completion.Task;
        public IdleWaiter(CatalogLibraryService service) { _service = service; service.StatusChanged += Changed; }
        private void Changed(object? sender, EventArgs args)
        {
            if (_service.Status.IsBusy) _wasBusy = true;
            else if (_wasBusy) _completion.TrySetResult();
        }
        public void Dispose() => _service.StatusChanged -= Changed;
    }
    private sealed class LocalLibrary : IGameLibraryService
    {
        public IReadOnlyList<GameEntry> Games { get; set; } = [Game(10)];
        public Func<CancellationToken, Task<IReadOnlyList<GameEntry>>>? Load { get; set; }
        public Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default) => Load?.Invoke(cancellationToken) ?? Task.FromResult(Games);
    }
    private sealed class Profile : ISteamVdfFallback
    {
        public string Account { get; set; } = AccountA.ToString();
        public string? GetCurrentUserSteamId() => Account;
        public IReadOnlyCollection<uint> GetInstalledAppIds() => [];
        public bool IsSubscribedFromFamilySharing(uint appId) => false;
        public IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps() => new Dictionary<uint, SteamAppDefinition>();
        public IReadOnlyList<SteamCollectionDefinition> GetCollections() => [];
    }
    private sealed record CatalogCall(uint[] Ids, string Language, CatalogNetworkMode Mode);
    private sealed class Catalog : ISteamCatalogService
    {
        public ConcurrentQueue<CatalogCall> Calls { get; } = new();
        public Func<uint[], string, CatalogNetworkMode, CancellationToken, Task<IReadOnlyDictionary<uint, CatalogLookupResult>>>? Fetch { get; set; }
        public Task<IReadOnlyDictionary<uint, CatalogLookupResult>> EnrichAsync(IEnumerable<uint> appIds, string language = "english", CatalogNetworkMode mode = CatalogNetworkMode.Offline, IProgress<CatalogProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var ids = appIds.ToArray();
            Calls.Enqueue(new(ids, language, mode));
            return Fetch?.Invoke(ids, language, mode, cancellationToken) ?? Task.FromResult(EmptyMetadata());
        }
    }
    private sealed record FamilyCall(ulong SteamId, string Language, CatalogNetworkMode Mode);
    private sealed class FamilySession : ISteamFamilySessionService
    {
        public SteamSessionIdentity? Identity { get; set; }
        public SteamSessionIdentity? CurrentIdentity => Identity;
        public ConcurrentQueue<FamilyCall> Calls { get; } = new();
        public Func<ulong, string, CatalogNetworkMode, CancellationToken, Task<FamilySnapshot>>? Snapshot { get; set; }
        public Func<IProgress<SteamLoginProgress>?, CancellationToken, Task<SteamSessionIdentity>>? Login { get; set; }
        public async Task<SteamSessionIdentity> LoginWithQrAsync(IProgress<SteamLoginProgress>? progress = null, CancellationToken cancellationToken = default)
            => Identity = await (Login?.Invoke(progress, cancellationToken) ?? Task.FromResult(new SteamSessionIdentity(AccountA, "test-account")));
        public Task LogoutAsync(CancellationToken cancellationToken = default) { Identity = null; return Task.CompletedTask; }
        public Task<FamilySnapshot> GetFamilySnapshotAsync(ulong steamId, string language = "english", CatalogNetworkMode mode = CatalogNetworkMode.Offline, CancellationToken cancellationToken = default)
        {
            Calls.Enqueue(new(steamId, language, mode));
            return Snapshot?.Invoke(steamId, language, mode, cancellationToken) ?? Task.FromResult(new FamilySnapshot
            {
                SteamId = steamId, Language = language, Status = FamilySnapshotStatus.NotAuthenticated
            });
        }
    }
}
