namespace SteamCatalog;

public enum SteamLoginStage { Connecting, AwaitingQrApproval, Authenticating, Connected, Cancelled, Failed, LoggedOut }
public enum FamilySnapshotStatus { Success, NotAuthenticated, Revoked, Error }
public enum FamilyAppAccess { Unknown, Owned, Shared, Excluded }

/// <summary>Challenge URLs are ephemeral credentials. Display them only in the login UI; never log or persist them.</summary>
public sealed record class SteamLoginProgress(
    SteamLoginStage Stage, string? ChallengeUrl = null, ulong? SteamId = null, string? MessageCode = null)
{
    public override string ToString() => $"SteamLoginProgress {{ Stage = {Stage}, ChallengeUrl = [redacted] }}";
}

public sealed record class SteamSessionIdentity(ulong SteamId, string AccountName);

public sealed record class FamilyApp
{
    public uint AppId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ProductType { get; init; } = "unknown";
    public IReadOnlyList<ulong> OwnerSteamIds { get; init; } = Array.Empty<ulong>();
    public FamilyAppAccess Access { get; init; }
    public int ExcludeReason { get; init; }
    public string ExcludeReasonName { get; init; } = string.Empty;
    // FamilyGroups reports entitlement/exclusion, not current copy locks or launchability.
    public bool IsCurrentAvailabilityKnown => false;
}

public sealed record class FamilySnapshot
{
    public ulong SteamId { get; init; }
    public string Language { get; init; } = "english";
    public FamilySnapshotStatus Status { get; init; }
    public bool HasFamily { get; init; }
    public ulong? FamilyGroupId { get; init; }
    public IReadOnlyList<FamilyApp> Apps { get; init; } = Array.Empty<FamilyApp>();
    public DateTimeOffset FetchedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsFromCache { get; init; }
    public bool IsStale { get; init; }
    public string? ErrorCode { get; init; }
}

public interface ISteamFamilySessionService
{
    SteamSessionIdentity? CurrentIdentity { get; }
    Task<SteamSessionIdentity> LoginWithQrAsync(IProgress<SteamLoginProgress>? progress = null, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<FamilySnapshot> GetFamilySnapshotAsync(ulong steamId, string language = "english",
        CatalogNetworkMode mode = CatalogNetworkMode.Offline, CancellationToken cancellationToken = default);
}

public interface ISteamFamilyTransport : IAsyncDisposable
{
    SteamSessionIdentity? CurrentIdentity { get; }
    Task<SteamSessionIdentity> LoginWithQrAsync(IProgress<SteamLoginProgress>? progress, CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
    Task<FamilySnapshot> FetchFamilyAsync(ulong steamId, string language, CancellationToken cancellationToken);
}

/// <summary>Implementation must use OS-protected storage or memory only, never plaintext disk storage.</summary>
public interface ISteamTokenStore
{
    bool IsPersistent { get; }
    ValueTask<SteamRefreshCredential?> ReadAsync(ulong steamId, CancellationToken cancellationToken = default);
    ValueTask WriteAsync(ulong steamId, SteamRefreshCredential credential, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(ulong steamId, CancellationToken cancellationToken = default);
}

public sealed class SteamRefreshCredential
{
    public SteamRefreshCredential(string accountName, string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        AccountName = accountName;
        RefreshToken = refreshToken;
    }
    public string AccountName { get; }
    public string RefreshToken { get; }
    public override string ToString() => "SteamRefreshCredential [redacted]";
}

public sealed class MemorySteamTokenStore : ISteamTokenStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, SteamRefreshCredential> _tokens = new();
    public bool IsPersistent => false;
    public ValueTask<SteamRefreshCredential?> ReadAsync(ulong steamId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_tokens.TryGetValue(steamId, out var credential) ? credential : null);
    }
    public ValueTask WriteAsync(ulong steamId, SteamRefreshCredential credential, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _tokens[steamId] = credential;
        return ValueTask.CompletedTask;
    }
    public ValueTask DeleteAsync(ulong steamId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _tokens.TryRemove(steamId, out _);
        return ValueTask.CompletedTask;
    }
}
