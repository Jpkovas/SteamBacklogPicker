using System.Runtime.CompilerServices;

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
            SteamHookMode.MemoryInspection => new SteamMemoryPollingHookClient(options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Mode), options.Mode, "Unsupported hook mode"),
        };

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
