using System.Globalization;
using System.Text.Json;
using Domain;
using SteamCatalog;
using SteamClientAdapter;

namespace SteamBacklogPicker.UI.Services.Library;

/// <summary>Publishes generation-bound snapshots and persists only identity-bound local discovery evidence.</summary>
public sealed class CatalogLibraryService : IGameLibraryService, ILibrarySynchronization, IDisposable
{
    private readonly IGameLibraryService _local;
    private readonly ISteamVdfFallback _profile;
    private readonly ISteamCatalogService _catalog;
    private readonly ISteamFamilySessionService _family;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _gate = new();
    private SynchronizationRun? _activeRun;
    private int _generation;
    private int _publishedGeneration = -1;
    private bool _networkEnabled;
    private string _language = "pt-BR";
    private LibrarySyncStatus _status = new();
    private IReadOnlyList<GameEntry> _snapshot = Array.Empty<GameEntry>();
    private string _snapshotAccount = "local";
    private bool _disposed;

    public CatalogLibraryService(IGameLibraryService local, ISteamVdfFallback profile, ISteamCatalogService catalog,
        ISteamFamilySessionService family, string? directory = null, TimeProvider? timeProvider = null)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _family = family ?? throw new ArgumentNullException(nameof(family));
        _time = timeProvider ?? TimeProvider.System;
        _directory = Path.GetFullPath(directory ?? Path.Combine(Path.GetDirectoryName(SqliteCatalogCache.GetDefaultPath())!, "snapshots"));
        _networkEnabled = ReadNetworkPreference();
    }

    public event EventHandler<LibrarySnapshotEventArgs>? SnapshotChanged;
    public event EventHandler? StatusChanged;
    public LibrarySyncStatus Status { get { lock (_gate) return _status; } }
    public bool NetworkEnabled
    {
        get { lock (_gate) return _networkEnabled; }
        set
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_networkEnabled == value) return;
                _networkEnabled = value;
                WriteNetworkPreferenceNoLock();
            }
            CancelSynchronization();
            UpdateCurrentStatus(status => status with { Message = Text(Language,
                value ? "Sincronização online ativada." : "Modo local: usando o cache disponível.",
                value ? "Online synchronization enabled." : "Local mode: using available cached data.") });
        }
    }
    public string Language
    {
        get { lock (_gate) return _language; }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (string.Equals(_language, value, StringComparison.OrdinalIgnoreCase)) return;
                _language = value;
            }
            CancelSynchronization();
        }
    }

    public async Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        var run = BeginRun(cancellationToken);
        var acquired = false;
        var backgroundStarted = false;
        try
        {
            await _refreshGate.WaitAsync(run.Token).ConfigureAwait(false);
            acquired = true;
            UpdateStatus(run, status => status with
            {
                IsBusy = true, QrChallengeUrl = null,
                Message = Text(run.Language, "Lendo biblioteca local…", "Reading local library…"), Error = null
            });

            IReadOnlyList<GameEntry> sourceLocal = Array.Empty<GameEntry>();
            string localAccount = "local";
            var stableIdentity = false;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var before = ReadLocalAccount();
                var accountBefore = EffectiveAccount(before);
                bool changedAccount;
                lock (_gate) changedAccount = _snapshot.Count > 0 && _snapshotAccount != accountBefore;
                if (changedAccount) ClearSnapshot(run, accountBefore);
                sourceLocal = NormalizeEntries(await _local.GetLibraryAsync(run.Token).ConfigureAwait(false));
                EnsureCurrent(run);
                localAccount = ReadLocalAccount();
                if (before == localAccount) { stableIdentity = true; break; }
            }
            if (!stableIdentity)
            {
                ClearSnapshot(run, EffectiveAccount(), "steam_profile_changed_during_refresh");
                run.Cancel();
                throw new OperationCanceledException("Steam profile changed during local discovery.", run.Token);
            }

            var account = EffectiveAccount(localAccount);
            if (sourceLocal.Count == 0)
            {
                var previous = await ReadSnapshotAsync(localAccount, run.Token).ConfigureAwait(false);
                sourceLocal = previous.Select(game => game with
                {
                    InstallState = InstallState.Unknown, OwnershipType = OwnershipType.Unknown
                }).ToArray();
            }
            var local = VisibleLocal(sourceLocal, localAccount, account);
            var cached = await _catalog.EnrichAsync(local.Select(game => game.SteamAppId ?? 0), run.SteamLanguage,
                CatalogNetworkMode.Offline, cancellationToken: run.Token).ConfigureAwait(false);
            var games = Enrich(local, cached, run.SteamLanguage);
            EnsureContext(run, localAccount, account);
            Publish(games, account, run);
            UpdateStatus(run, _ => new LibrarySyncStatus(account,
                Text(run.Language, "Biblioteca local carregada. Cobertura familiar pode ser parcial.",
                    "Local library loaded. Family coverage may be incomplete."),
                IsBusy: true, IsConnected: _family.CurrentIdentity is not null,
                LastUpdated: DateTimeOffset.Now, MissingNames: CountMissing(games), Generation: run.Generation));
            EnsureContext(run, localAccount, account);
            backgroundStarted = true;
            _ = Task.Run(() => SynchronizeAsync(sourceLocal, games, localAccount, account, run), CancellationToken.None);
            EnsureContext(run, localAccount, account);
            return games;
        }
        finally
        {
            if (acquired) _refreshGate.Release();
            if (!backgroundStarted) CompleteRun(run);
        }
    }

    private async Task SynchronizeAsync(IReadOnlyList<GameEntry> sourceLocal, IReadOnlyList<GameEntry> local,
        string localAccount, string account, SynchronizationRun run)
    {
        var games = local;
        var savedLocal = sourceLocal;
        string? synchronizationError = null;
        try
        {
            EnsureContext(run, localAccount, account);
            // Only local discovery is persisted here. Family evidence remains in its dedicated identity/TTL cache.
            await SaveSnapshotAsync(localAccount, sourceLocal, run).ConfigureAwait(false);
            if (ulong.TryParse(account, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId))
            {
                var mode = run.NetworkEnabled && _family.CurrentIdentity?.SteamId == steamId
                    ? CatalogNetworkMode.Online : CatalogNetworkMode.Offline;
                var family = await _family.GetFamilySnapshotAsync(steamId, run.SteamLanguage, mode, run.Token).ConfigureAwait(false);
                EnsureContext(run, localAccount, account);
                if (family.SteamId != steamId || family.Language != run.SteamLanguage)
                    synchronizationError = "family_response_identity_mismatch";
                else if (family.Status == FamilySnapshotStatus.Success
                    || family.Status == FamilySnapshotStatus.Error && family.IsFromCache)
                {
                    games = family.HasFamily ? MergeFamily(local, family) : ClearUnconfirmedFamilyOwnership(local);
                    Publish(games, account, run);
                    if (family.IsStale || family.Status == FamilySnapshotStatus.Error)
                        synchronizationError = family.ErrorCode ?? "family_snapshot_stale";
                }
                else if (family.Status is FamilySnapshotStatus.Revoked or FamilySnapshotStatus.NotAuthenticated)
                {
                    // Never reapply cached family apps on a negative authorization response.
                    games = ClearUnconfirmedFamilyOwnership(local);
                    Publish(games, account, run);
                    if (family.Status == FamilySnapshotStatus.Revoked) synchronizationError = family.ErrorCode ?? "family_access_revoked";
                }
                else synchronizationError = family.ErrorCode ?? "family_refresh_failed";
            }

            if (run.NetworkEnabled)
            {
                // Query every known ID so positive TTLs and locale changes work, prioritizing visible incomplete entries.
                // The catalog's fresh cache prevents unnecessary network calls for complete entries.
                var ids = games.OrderByDescending(game => IsMissingName(game) || game.ProductCategory == ProductCategory.Unknown)
                    .ThenByDescending(game => game.InstallState == InstallState.Installed)
                    .Select(game => game.SteamAppId ?? 0).Where(id => id > 0).Distinct().ToArray();
                var completed = 0;
                var view = new MetadataProjection(games);
                var localView = ReferenceEquals(games, sourceLocal) ? view : new MetadataProjection(sourceLocal);
                var lastSnapshot = _time.GetTimestamp();
                var lastProgress = lastSnapshot;
                foreach (var batch in ids.Chunk(20))
                {
                    EnsureContext(run, localAccount, account);
                    if (completed == 0 || _time.GetElapsedTime(lastProgress) >= ProgressInterval)
                    {
                        UpdateStatus(run, status => status with
                        {
                            IsBusy = true, Message = Text(run.Language,
                                $"Atualizando nomes e detalhes: {completed}/{ids.Length}", $"Updating names and details: {completed}/{ids.Length}")
                        });
                        lastProgress = _time.GetTimestamp();
                    }
                    var metadata = await _catalog.EnrichAsync(batch, run.SteamLanguage, CatalogNetworkMode.Online,
                        cancellationToken: run.Token).ConfigureAwait(false);
                    EnsureContext(run, localAccount, account);
                    view.Apply(metadata, run.SteamLanguage);
                    if (!ReferenceEquals(view, localView)) localView.Apply(metadata, run.SteamLanguage);
                    if (metadata.Values.Any(result => result.Status == CatalogLookupStatus.Unavailable || result.IsStale))
                        synchronizationError ??= "catalog_refresh_partial";
                    completed += batch.Length;
                    if (view.HasChanges && _time.GetElapsedTime(lastSnapshot) >= ProgressInterval)
                    {
                        games = view.Snapshot();
                        Publish(games, account, run);
                        lastSnapshot = _time.GetTimestamp();
                    }
                }
                EnsureContext(run, localAccount, account);
                games = view.Snapshot();
                sourceLocal = localView.Snapshot();
                Publish(games, account, run);
            }
            EnsureContext(run, localAccount, account);
            if (!ReferenceEquals(sourceLocal, savedLocal))
                await SaveSnapshotAsync(localAccount, sourceLocal, run).ConfigureAwait(false);
            UpdateStatus(run, status => status with
            {
                IsBusy = false, LastUpdated = DateTimeOffset.Now, MissingNames = CountMissing(games), Error = synchronizationError,
                IsConnected = _family.CurrentIdentity is not null,
                Message = synchronizationError is null
                    ? Text(run.Language, "Biblioteca pronta. O Steam confirma acesso e disponibilidade ao abrir.",
                        "Library ready. Steam confirms access and availability when opening.")
                    : Text(run.Language, "Atualização parcial. Dados anteriores disponíveis no cache foram preservados.",
                        "Partial refresh. Previous cached data has been preserved.")
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            UpdateStatus(run, status => status with
            {
                IsBusy = false, Error = ex is SteamLoginException ? "steam_session_error" : ex.GetType().Name,
                Message = Text(run.Language, "Atualização parcial. A biblioteca anterior foi preservada.",
                    "Partial refresh. Your previous library was preserved.")
            });
        }
        finally { CompleteRun(run); }
    }

    private static IReadOnlyList<GameEntry> VisibleLocal(IReadOnlyList<GameEntry> games, string localAccount, string account)
        => account == localAccount ? games : games.Where(game => game.InstallState == InstallState.Installed)
            .Select(game => game with { OwnershipType = OwnershipType.Unknown, Tags = Array.Empty<string>() }).ToArray();

    private static IReadOnlyList<GameEntry> Enrich(IReadOnlyList<GameEntry> games,
        IReadOnlyDictionary<uint, CatalogLookupResult> metadata, string language)
    {
        if (metadata.Count == 0) return games;
        GameEntry[]? updated = null;
        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            if (game.SteamAppId is not uint id || !metadata.TryGetValue(id, out var result)) continue;
            var entry = EnrichEntry(game, result, language);
            if (ReferenceEquals(entry, game)) continue;
            updated ??= games.ToArray();
            updated[index] = entry;
        }
        return updated ?? games;
    }

    private static GameEntry EnrichEntry(GameEntry game, CatalogLookupResult result, string language)
    {
        if (result.Status != CatalogLookupStatus.Found || result.Item is not { } item
            || item.AppId != game.SteamAppId || item.Language != language || string.IsNullOrWhiteSpace(item.Name)) return game;
        var category = Category(item.ProductType, game.ProductCategory);
        var platforms = new List<SteamPlatform>(3);
        if (item.Platforms.HasFlag(CatalogPlatforms.Windows)) platforms.Add(SteamPlatform.Windows);
        if (item.Platforms.HasFlag(CatalogPlatforms.Linux)) platforms.Add(SteamPlatform.Linux);
        if (item.Platforms.HasFlag(CatalogPlatforms.MacOS)) platforms.Add(SteamPlatform.MacOS);
        var samePlatforms = platforms.Count == 0 || platforms.Count == game.SupportedPlatforms.Count
            && platforms.All(platform => game.SupportedPlatforms.Contains(platform));
        if (game.Title == item.Name && game.ProductCategory == category && samePlatforms) return game;
        return game with
        {
            Title = item.Name, ProductCategory = category,
            SupportedPlatforms = samePlatforms ? game.SupportedPlatforms : platforms
        };
    }

    // Update only the IDs returned by each batch. Published arrays are never mutated by later batches.
    private sealed class MetadataProjection
    {
        private readonly GameEntry[] _entries;
        private readonly Dictionary<uint, int> _indexes;
        private IReadOnlyList<GameEntry> _snapshot;
        public bool HasChanges { get; private set; }
        public MetadataProjection(IReadOnlyList<GameEntry> games)
        {
            _entries = games.ToArray();
            _snapshot = games;
            _indexes = _entries.Select((game, index) => (Id: game.SteamAppId!.Value, Index: index))
                .ToDictionary(item => item.Id, item => item.Index);
        }
        public void Apply(IReadOnlyDictionary<uint, CatalogLookupResult> metadata, string language)
        {
            foreach (var (id, result) in metadata)
            {
                if (!_indexes.TryGetValue(id, out var index)) continue;
                var entry = EnrichEntry(_entries[index], result, language);
                if (ReferenceEquals(entry, _entries[index])) continue;
                _entries[index] = entry;
                HasChanges = true;
            }
        }
        public IReadOnlyList<GameEntry> Snapshot()
        {
            if (HasChanges) { _snapshot = _entries.ToArray(); HasChanges = false; }
            return _snapshot;
        }
    }

    internal static IReadOnlyList<GameEntry> MergeFamily(IReadOnlyList<GameEntry> local, FamilySnapshot family)
    {
        // A full Family response replaces prior sharing evidence, including local profile flags.
        // Keep installation evidence and owned entries; restore sharing only for included current rights.
        var entries = NormalizeEntries(ClearUnconfirmedFamilyOwnership(local)).ToDictionary(game => game.Id);
        foreach (var item in family.Apps)
        {
            if (item.AppId == 0 || item.Access == FamilyAppAccess.Excluded) continue;
            var id = GameIdentifier.ForSteam(item.AppId);
            var existing = entries.GetValueOrDefault(id);
            var ownership = family.IsStale || family.Status != FamilySnapshotStatus.Success
                ? existing?.OwnershipType == OwnershipType.Owned ? OwnershipType.Owned : OwnershipType.Unknown : item.Access switch
            {
                FamilyAppAccess.Owned => OwnershipType.Owned,
                FamilyAppAccess.Shared => OwnershipType.FamilyShared,
                _ => OwnershipType.Unknown
            };
            entries[id] = (existing ?? new GameEntry { Id = id, InstallState = InstallState.Available }) with
            {
                Title = string.IsNullOrWhiteSpace(item.Name) ? existing?.Title ?? $"App {item.AppId}" : item.Name,
                OwnershipType = ownership,
                ProductCategory = Category(item.ProductType, existing?.ProductCategory ?? ProductCategory.Unknown)
            };
        }
        return entries.Values.OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static IReadOnlyList<GameEntry> ClearUnconfirmedFamilyOwnership(IReadOnlyList<GameEntry> games)
        => games.Any(game => game.OwnershipType == OwnershipType.FamilyShared)
            ? games.Select(game => game.OwnershipType == OwnershipType.FamilyShared
                ? game with { OwnershipType = OwnershipType.Unknown } : game).ToArray() : games;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        NetworkEnabled = true;
        var run = BeginRun(cancellationToken);
        var acquired = false;
        try
        {
            await _refreshGate.WaitAsync(run.Token).ConfigureAwait(false);
            acquired = true;
            UpdateStatus(run, status => status with { IsBusy = true, QrChallengeUrl = null, Error = null });
            var identity = await _family.LoginWithQrAsync(new InlineProgress<SteamLoginProgress>(progress =>
            {
                UpdateStatus(run, status => status with
                {
                    IsBusy = true,
                    Message = progress.Stage == SteamLoginStage.AwaitingQrApproval
                        ? Text(run.Language, "Aguardando aprovação no Steam.", "Waiting for Steam approval.")
                        : Text(run.Language, "Conectando ao Steam…", "Connecting to Steam…"),
                    QrChallengeUrl = progress.ChallengeUrl, Error = progress.MessageCode
                });
            }), run.Token).ConfigureAwait(false);
            if (!IsCurrent(run))
            {
                // A transport must honor cancellation, but never retain a late successful login if it did not.
                await _family.LogoutAsync(CancellationToken.None).ConfigureAwait(false);
                throw new OperationCanceledException(run.Token);
            }
            var account = identity.SteamId.ToString(CultureInfo.InvariantCulture);
            lock (_gate)
            {
                if (_snapshotAccount != account) { _snapshot = Array.Empty<GameEntry>(); _snapshotAccount = account; }
            }
            Publish(CurrentSnapshot(account), account, run);
            UpdateStatus(run, _ => new LibrarySyncStatus(account,
                Text(run.Language, "Conta conectada.", "Account connected."), IsConnected: true, Generation: run.Generation));
        }
        finally
        {
            if (acquired) _refreshGate.Release();
            CompleteRun(run);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var run = BeginRun(cancellationToken);
        var acquired = false;
        try
        {
            await _refreshGate.WaitAsync(run.Token).ConfigureAwait(false);
            acquired = true;
            await _family.LogoutAsync(run.Token).ConfigureAwait(false);
            EnsureCurrent(run);
            var account = ReadLocalAccount();
            ClearSnapshot(run, account);
            UpdateStatus(run, _ => new LibrarySyncStatus(account,
                Text(run.Language, "Sessão encerrada.", "Signed out."), Generation: run.Generation));
        }
        finally
        {
            if (acquired) _refreshGate.Release();
            CompleteRun(run);
        }
    }

    public void CancelSynchronization()
    {
        SynchronizationRun? previous;
        lock (_gate)
        {
            if (_disposed) return;
            previous = _activeRun;
            _activeRun = null;
            _generation++;
            _status = _status with
            {
                Generation = _generation, IsBusy = false, QrChallengeUrl = null,
                Message = Text(_language, "Sincronização cancelada. Dados locais preservados.",
                    "Synchronization cancelled. Local data preserved.")
            };
        }
        previous?.Cancel();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private SynchronizationRun BeginRun(CancellationToken token)
    {
        SynchronizationRun? previous;
        SynchronizationRun run;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _activeRun;
            run = new SynchronizationRun(++_generation, token, _language, SteamLanguage(_language), _networkEnabled);
            _activeRun = run;
            _status = _status with { Generation = _generation, IsBusy = false, QrChallengeUrl = null };
        }
        previous?.Cancel();
        return run;
    }

    private bool IsCurrent(SynchronizationRun run)
    {
        lock (_gate) return !_disposed && run.Generation == _generation && !run.Token.IsCancellationRequested;
    }
    private void EnsureCurrent(SynchronizationRun run)
    {
        if (!IsCurrent(run)) throw new OperationCanceledException(run.Token);
    }
    private void EnsureContext(SynchronizationRun run, string localAccount, string account)
    {
        EnsureCurrent(run);
        var currentLocalAccount = ReadLocalAccount();
        var currentAccount = EffectiveAccount(currentLocalAccount);
        if (currentLocalAccount == localAccount && currentAccount == account) return;
        ClearSnapshot(run, currentAccount, "steam_identity_changed");
        run.Cancel();
        throw new OperationCanceledException(run.Token);
    }
    private void CompleteRun(SynchronizationRun run)
    {
        var changed = false;
        lock (_gate)
        {
            if (!_disposed && ReferenceEquals(_activeRun, run))
            {
                _activeRun = null;
                var next = _status with
                {
                    IsBusy = false, QrChallengeUrl = null, IsConnected = _family.CurrentIdentity is not null,
                    Message = run.Token.IsCancellationRequested
                        ? Text(run.Language, "Sincronização cancelada. Dados locais preservados.",
                            "Synchronization cancelled. Local data preserved.") : _status.Message
                };
                changed = next != _status;
                _status = next;
            }
        }
        run.Dispose();
        if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Publish(IReadOnlyList<GameEntry> games, string account, SynchronizationRun run)
    {
        lock (_gate)
        {
            if (_disposed || run.Generation != _generation || run.Token.IsCancellationRequested) return;
            if (_publishedGeneration == run.Generation && _snapshotAccount == account && ReferenceEquals(_snapshot, games)) return;
            _snapshot = games;
            _snapshotAccount = account;
            _publishedGeneration = run.Generation;
            _status = _status with { AccountId = account, Generation = run.Generation };
        }
        // Consumers also compare Generation when dispatching this event onto the UI thread.
        SnapshotChanged?.Invoke(this, new LibrarySnapshotEventArgs(games, account, run.Generation));
    }
    private IReadOnlyList<GameEntry> CurrentSnapshot(string account)
    {
        lock (_gate) return _snapshotAccount == account ? _snapshot : Array.Empty<GameEntry>();
    }
    private void ClearSnapshot(SynchronizationRun run, string account, string? error = null)
    {
        Publish(Array.Empty<GameEntry>(), account, run);
        UpdateStatus(run, status => status with { AccountId = account, Error = error, IsConnected = _family.CurrentIdentity is not null });
    }
    private void UpdateStatus(SynchronizationRun run, Func<LibrarySyncStatus, LibrarySyncStatus> update)
    {
        lock (_gate)
        {
            if (_disposed || run.Generation != _generation || run.Token.IsCancellationRequested) return;
            var next = update(_status) with { Generation = run.Generation };
            if (next == _status) return;
            _status = next;
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    private void UpdateCurrentStatus(Func<LibrarySyncStatus, LibrarySyncStatus> update)
    {
        lock (_gate) { if (_disposed) return; _status = update(_status) with { Generation = _generation }; }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private string ReadLocalAccount()
    {
        var id = _profile.GetCurrentUserSteamId();
        return ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value.ToString(CultureInfo.InvariantCulture) : "local";
    }
    private string EffectiveAccount(string? localAccount = null)
        => _family.CurrentIdentity?.SteamId.ToString(CultureInfo.InvariantCulture) ?? localAccount ?? ReadLocalAccount();
    private static string SteamLanguage(string language) => language.ToLowerInvariant() switch
    {
        "pt" or "pt-br" or "brazilian" => "brazilian", "pt-pt" or "portuguese" => "portuguese",
        "es" or "es-es" or "spanish" => "spanish", "de" or "de-de" or "german" => "german",
        "fr" or "fr-fr" or "french" => "french", _ => "english"
    };
    private static string Text(string language, string pt, string en)
        => language.StartsWith("pt", StringComparison.OrdinalIgnoreCase) || language == "brazilian" ? pt : en;
    private static ProductCategory Category(string? value, ProductCategory fallback) => value?.ToLowerInvariant() switch
    {
        "game" => ProductCategory.Game, "dlc" => ProductCategory.DLC, "music" or "soundtrack" => ProductCategory.Soundtrack,
        "application" or "software" => ProductCategory.Software, "tool" => ProductCategory.Tool, "video" => ProductCategory.Video,
        "demo" or "config" => ProductCategory.Other, _ => fallback
    };
    private static bool IsMissingName(GameEntry game) => string.IsNullOrWhiteSpace(game.Title) || game.Title.StartsWith("App ", StringComparison.Ordinal);
    private static int CountMissing(IEnumerable<GameEntry> games) => games.Count(IsMissingName);
    private static IReadOnlyList<GameEntry> NormalizeEntries(IEnumerable<GameEntry> games)
        => games.Where(game => game?.Id is { Storefront: Storefront.Steam, SteamAppId: > 0 })
            .GroupBy(game => game.SteamAppId!.Value).Select(group =>
            {
                var game = group.Last();
                // JSON can explicitly contain null despite non-nullable domain defaults.
                return game with
                {
                    Id = GameIdentifier.ForSteam(group.Key),
                    Title = string.IsNullOrWhiteSpace(game.Title) ? $"App {group.Key}" : game.Title,
                    Tags = game.Tags ?? Array.Empty<string>(),
                    StoreCategoryIds = game.StoreCategoryIds ?? Array.Empty<int>(),
                    SupportedPlatforms = game.SupportedPlatforms ?? Array.Empty<SteamPlatform>()
                };
            }).ToArray();

    private string SnapshotPath(string account)
    {
        if (account != "local" && (!ulong.TryParse(account, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0))
            throw new ArgumentException("Invalid account identity.", nameof(account));
        return Path.Combine(_directory, account + ".json");
    }
    private async Task<IReadOnlyList<GameEntry>> ReadSnapshotAsync(string account, CancellationToken token)
    {
        try
        {
            var path = SnapshotPath(account);
            if (!File.Exists(path) || new FileInfo(path).Length > 20 * 1024 * 1024) return Array.Empty<GameEntry>();
            var document = JsonSerializer.Deserialize<LocalSnapshot>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
            // Old arrays mixed local and Family data and cannot be safely attributed. Rebuild those from local discovery.
            return document is { Version: 1, Source: "local-discovery", Games: not null } && document.AccountId == account
                ? NormalizeEntries(document.Games) : Array.Empty<GameEntry>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Array.Empty<GameEntry>(); }
    }
    private async Task SaveSnapshotAsync(string localAccount, IReadOnlyList<GameEntry> localGames, SynchronizationRun run)
    {
        if (localGames.Count == 0) return;
        var path = SnapshotPath(localAccount);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            CreatePrivateDirectory();
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new LocalSnapshot(1, localAccount, "local-discovery", localGames)), run.Token).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            lock (_gate)
            {
                if (_disposed || run.Generation != _generation || run.Token.IsCancellationRequested || ReadLocalAccount() != localAccount) return;
                File.Move(temporary, path, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private bool ReadNetworkPreference()
    {
        try
        {
            var path = Path.Combine(_directory, "synchronization-preferences.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return true;
            return JsonSerializer.Deserialize<NetworkPreference>(File.ReadAllText(path)) is { Version: 1 } preference
                ? preference.NetworkEnabled : true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return true; }
    }
    private void WriteNetworkPreferenceNoLock()
    {
        var path = Path.Combine(_directory, "synchronization-preferences.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            CreatePrivateDirectory();
            File.WriteAllText(temporary, JsonSerializer.Serialize(new NetworkPreference(1, _networkEnabled)));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status = _status with { Error = "network_preference_save_failed" }; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private void CreatePrivateDirectory()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
        else Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public void Dispose()
    {
        SynchronizationRun? run;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            run = _activeRun;
            _activeRun = null;
        }
        run?.Cancel();
    }
    private sealed class SynchronizationRun : IDisposable
    {
        private readonly CancellationTokenSource _source;
        private readonly object _sync = new();
        private bool _isDisposed;
        public SynchronizationRun(int generation, CancellationToken token, string language, string steamLanguage, bool networkEnabled)
        {
            Generation = generation;
            _source = CancellationTokenSource.CreateLinkedTokenSource(token);
            Token = _source.Token;
            Language = language; SteamLanguage = steamLanguage; NetworkEnabled = networkEnabled;
        }
        public int Generation { get; }
        public CancellationToken Token { get; }
        public string Language { get; }
        public string SteamLanguage { get; }
        public bool NetworkEnabled { get; }
        public void Cancel() { lock (_sync) { if (!_isDisposed) _source.Cancel(); } }
        public void Dispose() { lock (_sync) { if (_isDisposed) return; _isDisposed = true; _source.Dispose(); } }
    }
    private sealed record LocalSnapshot(int Version, string AccountId, string Source, IReadOnlyList<GameEntry> Games);
    private sealed record NetworkPreference(int Version, bool NetworkEnabled);
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
