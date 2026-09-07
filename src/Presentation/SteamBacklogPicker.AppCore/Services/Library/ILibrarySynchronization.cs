using Domain;

namespace SteamBacklogPicker.UI.Services.Library;

public sealed record LibrarySyncStatus(string AccountId = "local", string Message = "", bool IsBusy = false,
    bool IsConnected = false, string? QrChallengeUrl = null, DateTimeOffset? LastUpdated = null,
    int MissingNames = 0, string? Error = null, int Generation = 0)
{
    public override string ToString() => $"LibrarySyncStatus {{ AccountId = {AccountId}, IsBusy = {IsBusy}, "
        + $"IsConnected = {IsConnected}, QrChallengeUrl = <redacted>, Generation = {Generation} }}";
}

public sealed class LibrarySnapshotEventArgs(IReadOnlyList<GameEntry> games, string accountId, int generation = 0) : EventArgs
{
    public IReadOnlyList<GameEntry> Games { get; } = games;
    public string AccountId { get; } = accountId;
    public int Generation { get; } = generation;
}

/// <summary>
/// Optional live synchronization capability. Every successful load publishes an identity/generation-bound
/// snapshot; consumers must use these events instead of replaying the identity-less GetLibraryAsync result.
/// Events can arrive on worker threads and must be checked against Status when dispatched to the UI.
/// </summary>
public interface ILibrarySynchronization
{
    event EventHandler<LibrarySnapshotEventArgs>? SnapshotChanged;
    event EventHandler? StatusChanged;
    LibrarySyncStatus Status { get; }
    bool NetworkEnabled { get; set; }
    string Language { get; set; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    void CancelSynchronization();
}
