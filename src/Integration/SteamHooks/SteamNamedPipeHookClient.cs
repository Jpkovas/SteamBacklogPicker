using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;

namespace SteamBacklogPicker.Integration.SteamHooks;

/// <summary>
/// Proof-of-concept hook that attaches to the undocumented Steam named pipe used by the legacy UI layer.
/// </summary>
public sealed class SteamNamedPipeHookClient : ISteamHookClient
{
    private readonly SteamHookOptions _options;
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public SteamNamedPipeHookClient(SteamHookOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SteamDownloadEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            if (!await TryConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                await Task.Delay(_options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Debug.Assert(_pipe is not null, "Pipe should be connected");
            try
            {
                using var reader = new StreamReader(
                    _pipe!,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 4096,
                    leaveOpen: true);
                using var writer = new StreamWriter(
                    _pipe!,
                    Encoding.UTF8,
                    bufferSize: 256,
                    leaveOpen: true)
                {
                    AutoFlush = true,
                };

                if (!string.IsNullOrEmpty(_options.HandshakePayload))
                {
                    await writer.WriteLineAsync(_options.HandshakePayload).ConfigureAwait(false);
                }

                while (!cancellationToken.IsCancellationRequested && _pipe!.IsConnected)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }
                    catch (IOException)
                    {
                        break;
                    }

                    if (line is null)
                    {
                        break;
                    }

                    if (TryParseEvent(line, out var downloadEvent))
                    {
                        yield return downloadEvent;
                    }
                }
            }
            finally
            {
                DisposePipe();
            }

            await Task.Delay(_options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        DisposePipe();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            DisposePipe();

            _pipe = new NamedPipeClientStream(
                ".",
                _options.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            var timeout = (int)Math.Clamp(_options.ConnectionTimeout.TotalMilliseconds, 100, int.MaxValue);
            await _pipe.ConnectAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            DisposePipe();
            return false;
        }
        catch (IOException)
        {
            DisposePipe();
            return false;
        }
        catch (OperationCanceledException)
        {
            DisposePipe();
            return false;
        }
    }

    private void DisposePipe()
    {
        if (_pipe is null)
        {
            return;
        }

        _pipe.Dispose();
        _pipe = null;
    }

    private bool TryParseEvent(string line, out SteamDownloadEvent downloadEvent)
    {
        downloadEvent = default!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var tokens = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var payload = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var kvp = token.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kvp.Length == 2)
            {
                payload.TryAdd(kvp[0], kvp[1]);
            }
        }

        if (!payload.TryGetValue("appid", out var appIdRaw)
            || !int.TryParse(appIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
        {
            return false;
        }

        payload.TryGetValue("depotid", out var depotRaw);
        payload.TryGetValue("status", out var statusRaw);
        payload.TryGetValue("progress", out var progressRaw);
        payload.TryGetValue("bytes", out var bytesRaw);

        if (_options.WatchedAppIds.Count > 0 && !_options.WatchedAppIds.Contains(appId))
        {
            return false;
        }

        int? depotId = int.TryParse(depotRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDepot)
            ? parsedDepot
            : null;
        double? progress = double.TryParse(
            progressRaw,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var parsedProgress)
            ? parsedProgress
            : null;
        long? bytes = long.TryParse(bytesRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBytes)
            ? parsedBytes
            : null;
        var status = string.IsNullOrWhiteSpace(statusRaw) ? "unknown" : statusRaw;

        downloadEvent = new SteamDownloadEvent(DateTimeOffset.UtcNow, appId, depotId, status, progress, bytes);
        return true;
    }
}
