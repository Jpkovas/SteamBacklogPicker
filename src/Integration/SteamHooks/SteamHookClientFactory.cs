using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Factory responsible for materializing the hook implementation requested in <see cref="SteamHookOptions"/>.
/// </summary>
public static class SteamHookClientFactory
{
    /// <summary>
    /// Creates a new <see cref="ISteamHookClient"/> for the supplied options.
    /// </summary>
    public static ISteamHookClient Create(SteamHookOptions options)
        => options.Mode switch
        {
            SteamHookMode.None => new NullSteamHookClient(),
            SteamHookMode.NamedPipe => new SteamNamedPipeHookClient(options),
            SteamHookMode.MemoryInspection => CreateMemoryClient(options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Mode), options.Mode, "Unsupported hook mode"),
        };

    private static ISteamHookClient CreateMemoryClient(SteamHookOptions options)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new SteamMemoryPollingHookClient(options);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && options.EnableUnsafeLinuxMemoryRead)
        {
            return new SteamMemoryPollingHookClient(options);
        }

        options.DiagnosticListener?.Invoke(
            SteamHookDiagnostic.Create(
                "steam_hook_memory_mode_degraded",
                new Dictionary<string, string>
                {
                    ["os"] = RuntimeInformation.OSDescription,
                    ["linux_mem_enabled"] = options.EnableUnsafeLinuxMemoryRead.ToString(),
                }));

        return new NullSteamHookClient();
    }

    private sealed class NullSteamHookClient : ISteamHookClient
    {
        public IAsyncEnumerable<SteamDownloadEvent> SubscribeAsync(CancellationToken cancellationToken = default)
        {
            return Empty(cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static async IAsyncEnumerable<SteamDownloadEvent> Empty([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
