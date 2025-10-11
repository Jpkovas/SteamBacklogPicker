namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Represents a download related event emitted by the Steam hook prototype.
/// </summary>
/// <param name="Timestamp">UTC timestamp when the event was observed.</param>
/// <param name="AppId">Steam application identifier associated with the event.</param>
/// <param name="DepotId">Optional depot identifier when the event is scoped to a depot.</param>
/// <param name="Status">Normalized status string (e.g. <c>completed</c>, <c>progress</c>).</param>
/// <param name="Progress">Progress value between 0 and 1, when available.</param>
/// <param name="BytesTransferred">Number of bytes transferred for the event (optional).</param>
public sealed record SteamDownloadEvent(
    DateTimeOffset Timestamp,
    int AppId,
    int? DepotId,
    string Status,
    double? Progress,
    long? BytesTransferred)
{
    /// <summary>
    /// Creates a completed download event for convenience.
    /// </summary>
    public static SteamDownloadEvent Completed(int appId, int? depotId = null)
        => new(DateTimeOffset.UtcNow, appId, depotId, "completed", 1d, null);
}
