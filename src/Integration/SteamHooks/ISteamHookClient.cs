namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Abstraction over a mechanism capable of surfacing Steam download events.
/// </summary>
public interface ISteamHookClient : IAsyncDisposable
{
    /// <summary>
    /// Streams download events as they are observed.
    /// </summary>
    IAsyncEnumerable<SteamDownloadEvent> SubscribeAsync(CancellationToken cancellationToken = default);
}
