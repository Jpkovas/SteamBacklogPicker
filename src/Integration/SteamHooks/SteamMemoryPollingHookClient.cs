using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Attempts to infer download state by inspecting the memory of the running <c>steam.exe</c> process.
/// </summary>
public sealed class SteamMemoryPollingHookClient : ISteamHookClient
{
    private readonly SteamHookOptions _options;
    private readonly Func<SteamHookOptions, Process?> _processFactory;
    private readonly Func<Process, SteamProcessAccessor?> _accessorFactory;

    public SteamMemoryPollingHookClient(
        SteamHookOptions options,
        Func<SteamHookOptions, Process?>? processFactory = null,
        Func<Process, SteamProcessAccessor?>? accessorFactory = null)
    {
        _options = options;
        _processFactory = processFactory ?? DefaultProcessFactory;
        _accessorFactory = accessorFactory ?? SteamProcessAccessor.TryCreate;
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
            return false;
        }

        using (process)
        {
            using var accessor = _accessorFactory(process);
            if (accessor is null)
            {
                return false;
            }

            return accessor.TryReadDownloadEvents(_options.MemoryScanAddresses, _options.MemoryReadLength, out events);
        }
    }

    public sealed class SteamProcessAccessor : IDisposable
    {
        private readonly SafeProcessHandle _processHandle;

        private SteamProcessAccessor(SafeProcessHandle processHandle)
        {
            _processHandle = processHandle;
        }

        public static SteamProcessAccessor? TryCreate(Process process)
        {
            var access = NativeMethods.ProcessAccessFlags.VirtualMemoryRead | NativeMethods.ProcessAccessFlags.QueryInformation;
            var handle = NativeMethods.OpenProcess(access, false, process.Id);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }

            return new SteamProcessAccessor(handle);
        }

        public bool TryReadDownloadEvents(ImmutableArray<nuint> addresses, int readLength, out ImmutableArray<SteamDownloadEvent> events)
        {
            var builder = ImmutableArray.CreateBuilder<SteamDownloadEvent>();
            if (addresses.IsDefaultOrEmpty)
            {
                events = builder.MoveToImmutable();
                return false;
            }

            var buffer = new byte[Math.Clamp(readLength, 32, 16 * 1024)];
            foreach (var address in addresses)
            {
                if (address == 0)
                {
                    continue;
                }

                if (!NativeMethods.ReadProcessMemory(_processHandle, (IntPtr)address, buffer, buffer.Length, out var bytesRead) || bytesRead == 0)
                {
                    continue;
                }

                if (TryParseSnapshot(buffer.AsSpan(0, bytesRead), out var snapshotEvents))
                {
                    builder.AddRange(snapshotEvents);
                }
            }

            events = builder.MoveToImmutable();
            return events.Length > 0;
        }

        private static bool TryParseSnapshot(ReadOnlySpan<byte> data, out IEnumerable<SteamDownloadEvent> events)
        {
            events = Enumerable.Empty<SteamDownloadEvent>();
            var text = Encoding.UTF8.GetString(data).TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var results = new List<SteamDownloadEvent>();
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in fields)
                {
                    var kvp = field.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (kvp.Length == 2)
                    {
                        fieldMap[kvp[0]] = kvp[1];
                    }
                }

                if (!fieldMap.TryGetValue("appid", out var appIdRaw) || !int.TryParse(appIdRaw, out var appId))
                {
                    continue;
                }

                fieldMap.TryGetValue("status", out var statusRaw);
                fieldMap.TryGetValue("progress", out var progressRaw);
                fieldMap.TryGetValue("bytes", out var bytesRaw);
                fieldMap.TryGetValue("depotid", out var depotRaw);

                var downloadEvent = new SteamDownloadEvent(
                    DateTimeOffset.UtcNow,
                    appId,
                    int.TryParse(depotRaw, out var depotId) ? depotId : null,
                    string.IsNullOrWhiteSpace(statusRaw) ? "unknown" : statusRaw!,
                    double.TryParse(progressRaw, out var progress) ? progress : null,
                    long.TryParse(bytesRaw, out var bytes) ? bytes : null);

                results.Add(downloadEvent);
            }

            events = results;
            return results.Count > 0;
        }

        public void Dispose()
        {
            _processHandle.Dispose();
        }
    }

    private static class NativeMethods
    {
        [Flags]
        public enum ProcessAccessFlags : uint
        {
            QueryInformation = 0x0400,
            VirtualMemoryRead = 0x0010,
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeProcessHandle OpenProcess(ProcessAccessFlags processAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            SafeProcessHandle hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            int dwSize,
            out int lpNumberOfBytesRead);
    }

    private static Process? DefaultProcessFactory(SteamHookOptions options)
        => Process.GetProcessesByName(options.ProcessName).FirstOrDefault();
}
