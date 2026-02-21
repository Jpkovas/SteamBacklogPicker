using System.Collections.Immutable;

namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Configuration for the optional Steam hook prototype.
/// </summary>
public sealed record SteamHookOptions
{
    /// <summary>
    /// Gets the hooking strategy that should be used.
    /// </summary>
    public SteamHookMode Mode { get; init; } = SteamHookMode.NamedPipe;

    /// <summary>
    /// Gets the Steam process name to probe.
    /// </summary>
    public string ProcessName { get; init; } = "steam";

    /// <summary>
    /// Gets the name of the named pipe exposed by Steam.
    /// </summary>
    public string PipeName { get; init; } = "steam.ipc";

    /// <summary>
    /// Gets the handshake payload that will be sent once the pipe connection is established.
    /// </summary>
    public string? HandshakePayload { get; init; }
        = "\"SteamBacklogPicker\":\"downloads\"";

    /// <summary>
    /// Gets the maximum time SteamBacklogPicker will wait while connecting to the pipe.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the delay used before re-attempting to attach to the pipe when the connection is interrupted.
    /// </summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the polling interval used when <see cref="SteamHookMode.MemoryInspection"/> is selected.
    /// </summary>
    public TimeSpan MemoryPollingInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the memory regions that should be inspected for download snapshots.
    /// </summary>
    public ImmutableArray<nuint> MemoryScanAddresses { get; init; } = ImmutableArray<nuint>.Empty;

    /// <summary>
    /// Gets or sets the number of bytes read from each memory region.
    /// </summary>
    public int MemoryReadLength { get; init; } = 1024;

    /// <summary>
    /// Gets the list of application identifiers to filter on. When empty the hook will report all events.
    /// </summary>
    public ImmutableHashSet<int> WatchedAppIds { get; init; } = ImmutableHashSet<int>.Empty;

    /// <summary>
    /// Gets a value indicating whether Linux process-memory probing can use <c>/proc/&lt;pid&gt;/mem</c>.
    /// </summary>
    /// <remarks>
    /// This option is disabled by default because most environments deny access unless elevated capabilities are granted,
    /// and Steam/VAC policies may treat aggressive probing as suspicious behavior.
    /// </remarks>
    public bool EnableUnsafeLinuxMemoryRead { get; init; }

    /// <summary>
    /// Gets an optional callback that receives structured diagnostics emitted by experimental hook flows.
    /// </summary>
    public Action<SteamHookDiagnostic>? DiagnosticListener { get; init; }
}
