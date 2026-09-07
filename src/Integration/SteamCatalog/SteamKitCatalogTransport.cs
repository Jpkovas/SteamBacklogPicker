using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace SteamCatalog;

/// <summary>
/// Real SteamKit 3.3.1 transport. Construction is offline; only online metadata requests or explicit QR login connect.
/// No Steam client cookies, credentials, authentication files or appcache files are read or changed.
/// </summary>
public sealed class SteamKitCatalogTransport : ISteamFamilyTransport, ICatalogMetadataSource, IDisposable
{
    private readonly ISteamTokenStore _tokens;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _requestTimeout;
    // SteamKit's default is derived from the bind address. Use an independent session ID alongside the Steam desktop client.
    private readonly uint _loginId = (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private Connection? _connection;
    private SteamSessionIdentity? _identity;
    private bool _disposed;

    public SteamKitCatalogTransport(ISteamTokenStore? tokenStore = null, TimeSpan? requestTimeout = null)
    {
        _tokens = tokenStore ?? new MemorySteamTokenStore();
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public SteamSessionIdentity? CurrentIdentity => Volatile.Read(ref _identity);

    public async Task<SteamSessionIdentity> LoginWithQrAsync(IProgress<SteamLoginProgress>? progress, CancellationToken cancellationToken)
    {
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = operationCancellation.Token;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await ClearIdentityAsync(CancellationToken.None).ConfigureAwait(false);
            await ResetConnectionAsync().ConfigureAwait(false);
            progress?.Report(new SteamLoginProgress(SteamLoginStage.Connecting));
            using var loginTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            loginTimeout.CancelAfter(TimeSpan.FromMinutes(3));
            var token = loginTimeout.Token;
            var connection = await GetConnectionAsync(token).ConfigureAwait(false);
            var session = await connection.Client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails
            {
                DeviceFriendlyName = "Steam Backlog Picker",
                IsPersistentSession = _tokens.IsPersistent,
                PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient
            }).WaitAsync(_requestTimeout, token).ConfigureAwait(false);

            void ReportChallenge() => progress?.Report(new SteamLoginProgress(
                SteamLoginStage.AwaitingQrApproval, session.ChallengeURL));
            session.ChallengeURLChanged = ReportChallenge;
            try
            {
                ReportChallenge();
                var authentication = await session.PollingWaitForResultAsync(token).ConfigureAwait(false);
                progress?.Report(new SteamLoginProgress(SteamLoginStage.Authenticating));
                var loggedOn = await connection.LogOnAsync(new SteamUser.LogOnDetails
                {
                    Username = authentication.AccountName,
                    AccessToken = authentication.RefreshToken,
                    LoginID = _loginId,
                    ShouldRememberPassword = _tokens.IsPersistent
                }, _requestTimeout, token).ConfigureAwait(false);
                if (loggedOn.ClientSteamID is null || !loggedOn.ClientSteamID.IsIndividualAccount)
                    throw new SteamProtocolException("steam_login_invalid_identity", false);
                var identity = new SteamSessionIdentity(loggedOn.ClientSteamID.ConvertToUInt64(), authentication.AccountName);
                Volatile.Write(ref _identity, identity);
                await _tokens.WriteAsync(identity.SteamId,
                    new SteamRefreshCredential(authentication.AccountName, authentication.RefreshToken), token).ConfigureAwait(false);
                progress?.Report(new SteamLoginProgress(SteamLoginStage.Connected, SteamId: identity.SteamId));
                return identity;
            }
            finally { session.ChallengeURLChanged = null; }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ClearIdentityAsync(CancellationToken.None).ConfigureAwait(false);
            await ResetConnectionAsync().ConfigureAwait(false);
            progress?.Report(new SteamLoginProgress(SteamLoginStage.Cancelled));
            throw;
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            await ClearIdentityAsync(CancellationToken.None).ConfigureAwait(false);
            await ResetConnectionAsync().ConfigureAwait(false);
            var code = ex is SteamProtocolException protocol ? protocol.ErrorCode : "steam_login_failed_or_timed_out";
            progress?.Report(new SteamLoginProgress(SteamLoginStage.Failed, MessageCode: code));
            // Do not forward exception payloads that could include session URLs, account details or tokens.
            throw new SteamLoginException(code);
        }
        finally { _gate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = operationCancellation.Token;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ClearIdentityAsync(CancellationToken.None).ConfigureAwait(false);
            await ResetConnectionAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<FamilySnapshot> FetchFamilyAsync(ulong steamId, string language, CancellationToken cancellationToken)
    {
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = operationCancellation.Token;
        var normalized = CatalogLanguage.Normalize(language);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (CurrentIdentity?.SteamId != steamId)
                return Failure(steamId, normalized, FamilySnapshotStatus.NotAuthenticated, "identity_requires_explicit_login");
            var connection = await EnsureLoggedOnAsync(true, cancellationToken).ConfigureAwait(false);
            var family = connection.Client.GetHandler<SteamUnifiedMessages>()!.CreateService<FamilyGroups>();
            var group = await family.GetFamilyGroupForUser(new CFamilyGroups_GetFamilyGroupForUser_Request
            {
                steamid = steamId,
                include_family_group_response = false
            }).ToTask().WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false);
            if (group.Result != EResult.OK) return await MapFamilyFailureAsync(steamId, normalized, group.Result).ConfigureAwait(false);
            if (group.Body.is_not_member_of_any_group)
                return new FamilySnapshot { SteamId = steamId, Language = normalized, Status = FamilySnapshotStatus.Success, HasFamily = false };
            if (group.Body.family_groupid == 0)
                return Failure(steamId, normalized, FamilySnapshotStatus.Error, "family_group_response_incomplete");

            var shared = await family.GetSharedLibraryApps(new CFamilyGroups_GetSharedLibraryApps_Request
            {
                family_groupid = group.Body.family_groupid,
                steamid = steamId,
                language = normalized,
                include_own = true,
                include_excluded = true,
                include_non_games = true
                // max_apps deliberately omitted: request the complete Family API result, without truncation.
            }).ToTask().WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false);
            if (shared.Result != EResult.OK) return await MapFamilyFailureAsync(steamId, normalized, shared.Result).ConfigureAwait(false);
            return MapFamilyApps(steamId, normalized, group.Body.family_groupid, shared.Body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            throw;
        }
        catch (SteamProtocolException ex) when (ex.IsRevoked)
        {
            await ClearIdentityAsync(CancellationToken.None).ConfigureAwait(false);
            await ResetConnectionAsync().ConfigureAwait(false);
            return Failure(steamId, normalized, FamilySnapshotStatus.Revoked, ex.ErrorCode);
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            return Failure(steamId, normalized, FamilySnapshotStatus.Error, "family_network_or_protocol_error");
        }
        finally { _gate.Release(); }
    }

    public async Task<MetadataResponse> FetchAsync(uint appId, string language, CancellationToken cancellationToken)
    {
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = operationCancellation.Token;
        if (appId == 0) throw new ArgumentOutOfRangeException(nameof(appId));
        var normalized = CatalogLanguage.Normalize(language);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var connection = await EnsureLoggedOnAsync(false, cancellationToken).ConfigureAwait(false);
            var apps = connection.Client.GetHandler<SteamApps>()!;
            var result = await apps.PICSGetProductInfo(new SteamApps.PICSRequest(appId), null)
                .ToTask().WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false);
            if (result.Failed || !result.Complete || result.Results is null)
                return new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, ErrorCode: "pics_incomplete");
            foreach (var part in result.Results)
            {
                if (!part.Apps.TryGetValue(appId, out var info)) continue;
                if (info.MissingToken || info.KeyValues is null)
                    return new MetadataResponse(CatalogLookupStatus.Unavailable, ErrorCode: "pics_access_token_required");
                return MapProductInfo(appId, normalized, info.KeyValues);
            }
            return new MetadataResponse(CatalogLookupStatus.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            throw;
        }
        catch (SteamProtocolException ex) when (ex.IsRevoked)
        {
            await ClearIdentityAsync(CancellationToken.None).ConfigureAwait(false);
            await ResetConnectionAsync().ConfigureAwait(false);
            return new MetadataResponse(CatalogLookupStatus.Unavailable, ErrorCode: "steam_session_revoked");
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            return new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, ErrorCode: "pics_network_or_protocol_error");
        }
        finally { _gate.Release(); }
    }

    public static MetadataResponse MapProductInfo(uint appId, string language, KeyValue root)
    {
        var common = root["common"];
        var name = common["name_localized"][language].Value;
        if (string.IsNullOrWhiteSpace(name)) name = common["name"].Value;
        if (string.IsNullOrWhiteSpace(name)) return new MetadataResponse(CatalogLookupStatus.Unavailable, ErrorCode: "pics_name_missing");
        var platforms = CatalogPlatforms.Unknown;
        foreach (var os in (common["oslist"].Value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            platforms |= os.ToLowerInvariant() switch
            {
                "windows" => CatalogPlatforms.Windows,
                "linux" => CatalogPlatforms.Linux,
                "macos" => CatalogPlatforms.MacOS,
                _ => CatalogPlatforms.Unknown
            };
        }
        return new MetadataResponse(CatalogLookupStatus.Found, new CatalogItem
        {
            AppId = appId, Language = CatalogLanguage.Normalize(language), Name = name.Trim(),
            ProductType = common["type"].Value?.ToLowerInvariant() ?? "unknown", Platforms = platforms,
            HeaderImageUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg", Source = "steam-pics"
        });
    }

    public static FamilySnapshot MapFamilyApps(ulong steamId, string language, ulong familyGroupId,
        CFamilyGroups_GetSharedLibraryApps_Response response)
    {
        // Preserve exclusions and ownership evidence; do not turn a shareable license into a claim that a copy is free now.
        var mapped = response.apps.Where(app => app.appid != 0).Select(app =>
        {
            var owners = app.owner_steamids.Where(id => id != 0).Distinct().ToArray();
            var owned = owners.Contains(steamId);
            var excluded = app.exclude_reason != ESharedLibraryExcludeReason.k_ESharedLibrary_Included;
            return new FamilyApp
            {
                AppId = app.appid, Name = app.name ?? string.Empty,
                ProductType = NormalizeAppType(app.app_type), OwnerSteamIds = owners,
                Access = owned ? FamilyAppAccess.Owned : excluded ? FamilyAppAccess.Excluded
                    : owners.Length > 0 ? FamilyAppAccess.Shared : FamilyAppAccess.Unknown,
                ExcludeReason = (int)app.exclude_reason, ExcludeReasonName = app.exclude_reason.ToString()
            };
        }).ToArray();
        return new FamilySnapshot
        {
            SteamId = steamId, Language = CatalogLanguage.Normalize(language), Status = FamilySnapshotStatus.Success,
            HasFamily = true, FamilyGroupId = familyGroupId, Apps = mapped
        };
    }

    private static string NormalizeAppType(EProtoAppType type)
    {
        var name = type.ToString();
        const string prefix = "k_EAppType";
        return (name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name).ToLowerInvariant();
    }

    private async Task<Connection> GetConnectionAsync(CancellationToken token)
    {
        if (_connection is { IsConnected: true }) return _connection;
        await ResetConnectionAsync().ConfigureAwait(false);
        _connection = new Connection();
        await _connection.ConnectAsync(_requestTimeout, token).ConfigureAwait(false);
        return _connection;
    }

    private async Task<Connection> EnsureLoggedOnAsync(bool requireIdentity, CancellationToken token)
    {
        var connection = await GetConnectionAsync(token).ConfigureAwait(false);
        if (connection.IsLoggedOn && (!requireIdentity || connection.LoggedOnSteamId == CurrentIdentity?.SteamId)) return connection;
        if (CurrentIdentity is { } identity)
        {
            var credential = await _tokens.ReadAsync(identity.SteamId, token).ConfigureAwait(false);
            if (credential is null) throw new SteamProtocolException("steam_session_credential_missing", true);
            var callback = await connection.LogOnAsync(new SteamUser.LogOnDetails
            {
                Username = credential.AccountName,
                AccessToken = credential.RefreshToken,
                ShouldRememberPassword = _tokens.IsPersistent,
                LoginID = _loginId
            }, _requestTimeout, token).ConfigureAwait(false);
            if (callback.ClientSteamID?.ConvertToUInt64() != identity.SteamId)
                throw new SteamProtocolException("steam_session_identity_mismatch", true);
        }
        else
        {
            if (requireIdentity) throw new SteamProtocolException("steam_login_required", true);
            await connection.LogOnAnonymousAsync(_requestTimeout, token).ConfigureAwait(false);
        }
        return connection;
    }

    private async Task<FamilySnapshot> MapFamilyFailureAsync(ulong steamId, string language, EResult result)
    {
        if (IsAuthenticationRejection(result))
        {
            await ClearIdentityAsync(CancellationToken.None).ConfigureAwait(false);
            await ResetConnectionAsync().ConfigureAwait(false);
            return Failure(steamId, language, FamilySnapshotStatus.Revoked, "steam_access_revoked_" + result);
        }
        return Failure(steamId, language, FamilySnapshotStatus.Error, "steam_family_result_" + result);
    }

    private static bool IsAuthenticationRejection(EResult result)
        => result is EResult.AccessDenied or EResult.NotLoggedOn or EResult.InvalidPassword or EResult.Expired or EResult.Revoked;

    private static FamilySnapshot Failure(ulong steamId, string language, FamilySnapshotStatus status, string code)
        => new() { SteamId = steamId, Language = language, Status = status, ErrorCode = code };

    private async Task ClearIdentityAsync(CancellationToken token)
    {
        var identity = Interlocked.Exchange(ref _identity, null);
        if (identity is not null) await _tokens.DeleteAsync(identity.SteamId, token).ConfigureAwait(false);
    }

    private async Task ResetConnectionAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(BeginDisposal());

    public void Dispose() => _ = ObserveShutdownAsync(BeginDisposal());

    private Task BeginDisposal()
    {
        lock (_disposeSync)
        {
            if (_disposeTask is not null) return _disposeTask;
            _lifetime.Cancel();
            Volatile.Read(ref _connection)?.RequestStop();
            return _disposeTask = DisposeCoreAsync();
        }
    }

    private static async Task ObserveShutdownAsync(Task shutdown)
    {
        try { await shutdown.ConfigureAwait(false); }
        catch { /* Synchronous host shutdown cannot await optional transport teardown. No secrets are logged. */ }
    }

    private async Task DisposeCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            try { await ClearIdentityAsync(CancellationToken.None).ConfigureAwait(false); }
            finally { await ResetConnectionAsync().ConfigureAwait(false); }
        }
        finally { _gate.Release(); }
    }

    private sealed class SteamProtocolException(string code, bool revoked) : Exception(code)
    {
        public string ErrorCode { get; } = code;
        public bool IsRevoked { get; } = revoked;
    }

    private sealed class Connection : IAsyncDisposable
    {
        private readonly CallbackManager _callbacks;
        private readonly CancellationTokenSource _pumpCancellation = new();
        private readonly object _stopSync = new();
        private bool _stopRequested;
        private readonly List<IDisposable> _subscriptions = new();
        private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<SteamUser.LoggedOnCallback>? _loggedOn;
        private readonly Task _pump;
        private volatile bool _isConnected;
        private volatile bool _isLoggedOn;
        public SteamClient Client { get; } = new();
        public bool IsConnected => _isConnected;
        public bool IsLoggedOn => _isLoggedOn;
        public ulong? LoggedOnSteamId { get; private set; }

        public Connection()
        {
            _callbacks = new CallbackManager(Client);
            _subscriptions.Add(_callbacks.Subscribe<SteamClient.ConnectedCallback>(_ =>
            {
                _isConnected = true;
                _connected.TrySetResult(true);
            }));
            _subscriptions.Add(_callbacks.Subscribe<SteamClient.DisconnectedCallback>(_ =>
            {
                _isConnected = false;
                _isLoggedOn = false;
                var error = new SteamProtocolException("steam_disconnected", false);
                _connected.TrySetException(error);
                _loggedOn?.TrySetException(error);
            }));
            _subscriptions.Add(_callbacks.Subscribe<SteamUser.LoggedOnCallback>(callback =>
            {
                _isLoggedOn = callback.Result == EResult.OK;
                LoggedOnSteamId = _isLoggedOn ? callback.ClientSteamID?.ConvertToUInt64() : null;
                if (_isLoggedOn) _loggedOn?.TrySetResult(callback);
                else _loggedOn?.TrySetException(new SteamProtocolException("steam_logon_" + callback.Result, IsAuthenticationRejection(callback.Result)));
            }));
            _subscriptions.Add(_callbacks.Subscribe<SteamUser.LoggedOffCallback>(_ => _isLoggedOn = false));
            _pump = Task.Run(() =>
            {
                while (!_pumpCancellation.IsCancellationRequested)
                    _callbacks.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
            });
        }

        public async Task ConnectAsync(TimeSpan timeout, CancellationToken token)
        {
            Client.Connect();
            await _connected.Task.WaitAsync(timeout, token).ConfigureAwait(false);
        }

        public Task<SteamUser.LoggedOnCallback> LogOnAsync(SteamUser.LogOnDetails details, TimeSpan timeout, CancellationToken token)
        {
            _loggedOn = new TaskCompletionSource<SteamUser.LoggedOnCallback>(TaskCreationOptions.RunContinuationsAsynchronously);
            Client.GetHandler<SteamUser>()!.LogOn(details);
            return _loggedOn.Task.WaitAsync(timeout, token);
        }

        public Task<SteamUser.LoggedOnCallback> LogOnAnonymousAsync(TimeSpan timeout, CancellationToken token)
        {
            _loggedOn = new TaskCompletionSource<SteamUser.LoggedOnCallback>(TaskCreationOptions.RunContinuationsAsynchronously);
            Client.GetHandler<SteamUser>()!.LogOnAnonymous();
            return _loggedOn.Task.WaitAsync(timeout, token);
        }

        public async ValueTask DisposeAsync()
        {
            RequestStop();
            await _pump.ConfigureAwait(false);
            foreach (var subscription in _subscriptions) subscription.Dispose();
            lock (_stopSync) _pumpCancellation.Dispose();
        }

        public void RequestStop()
        {
            lock (_stopSync)
            {
                if (_stopRequested) return;
                _stopRequested = true;
                _pumpCancellation.Cancel();
                Client.Disconnect();
            }
        }
    }
}

public sealed class SteamLoginException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
