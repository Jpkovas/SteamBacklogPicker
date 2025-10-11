namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Defines the strategy used to observe Steam runtime state.
/// </summary>
public enum SteamHookMode
{
    /// <summary>
    /// Disables the hook entirely.
    /// </summary>
    None = 0,

    /// <summary>
    /// Uses a named pipe exposed by the Steam client to react to events.
    /// </summary>
    NamedPipe,

    /// <summary>
    /// Polls the <c>steam.exe</c> process memory for download state.
    /// </summary>
    MemoryInspection
}
