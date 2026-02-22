using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Attempts to infer download state by inspecting the memory of the running <c>steam</c> process.
/// </summary>
public sealed partial class SteamMemoryPollingHookClient : ISteamHookClient
{
    private readonly SteamHookOptions _options;
    private readonly Func<SteamHookOptions, Process?> _processFactory;
    private readonly Func<Process, ISteamProcessMemoryReader?> _readerFactory;

    public SteamMemoryPollingHookClient(
        SteamHookOptions options,
        Func<SteamHookOptions, Process?>? processFactory = null,
        Func<Process, ISteamProcessMemoryReader?>? readerFactory = null)
    {
        _options = options;
        _processFactory = processFactory ?? DefaultProcessFactory;
        _readerFactory = readerFactory ?? CreateDefaultReader;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SteamDownloadEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_options.MemoryPollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(_options.MemoryPollingInterval), "Polling interval must be positive.");
        }

        using var timer = new PeriodicTimer(_options.MemoryPollingInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!TryCaptureDownloadState(out var events))
            {
                continue;
            }

            foreach (var downloadEvent in events)
            {
                if (_options.WatchedAppIds.Count == 0 || _options.WatchedAppIds.Contains(downloadEvent.AppId))
                {
                    yield return downloadEvent;
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private bool TryCaptureDownloadState(out ImmutableArray<SteamDownloadEvent> events)
    {
        events = ImmutableArray<SteamDownloadEvent>.Empty;

        var process = _processFactory(_options);
        if (process is null)
        {
            ReportDiagnostic("steam_hook_memory_process_not_found");
            return false;
        }

        using (process)
        {
            using var reader = _readerFactory(process);
            if (reader is null)
            {
                ReportDiagnostic(
                    "steam_hook_memory_reader_unavailable",
                    new Dictionary<string, string>
                    {
                        ["os"] = RuntimeInformation.OSDescription,
                    });

                return false;
            }

            var foundAny = false;
            var builder = ImmutableArray.CreateBuilder<SteamDownloadEvent>();
            var readBufferLength = Math.Clamp(_options.MemoryReadLength, 32, 16 * 1024);

            foreach (var address in _options.MemoryScanAddresses)
            {
                if (address == 0)
                {
                    continue;
                }

                if (!reader.TryReadMemory(address, readBufferLength, out var data) || data.Length == 0)
                {
                    continue;
                }

                foundAny = true;
                if (SteamSnapshotParser.TryParseSnapshot(data, out var parsed))
                {
                    builder.AddRange(parsed);
                }
            }

            events = builder.MoveToImmutable();
            if (!foundAny)
            {
                ReportDiagnostic("steam_hook_memory_snapshot_empty");
            }

            return events.Length > 0;
        }
    }

    private ISteamProcessMemoryReader? CreateDefaultReader(Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return WindowsSteamProcessMemoryReader.TryCreate(process);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!_options.EnableUnsafeLinuxMemoryRead)
            {
                ReportDiagnostic(
                    "steam_hook_linux_memory_read_disabled",
                    new Dictionary<string, string>
                    {
                        ["reason"] = "feature_flag_disabled",
                    });

                return null;
            }

            return LinuxProcMemReader.TryCreate(process, _options.DiagnosticListener);
        }

        ReportDiagnostic(
            "steam_hook_memory_os_not_supported",
            new Dictionary<string, string>
            {
                ["os"] = RuntimeInformation.OSDescription,
            });

        return null;
    }

    private void ReportDiagnostic(string eventName, IDictionary<string, string>? properties = null)
        => _options.DiagnosticListener?.Invoke(SteamHookDiagnostic.Create(eventName, properties));

    private static Process? DefaultProcessFactory(SteamHookOptions options)
        => Process.GetProcessesByName(options.ProcessName).FirstOrDefault();
}
