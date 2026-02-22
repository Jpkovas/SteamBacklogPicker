using System.Diagnostics;

namespace SteamBacklogPicker.Integration.SteamHooks;

internal sealed class LinuxProcMemReader : ISteamProcessMemoryReader
{
    private readonly FileStream _stream;

    private LinuxProcMemReader(FileStream stream)
    {
        _stream = stream;
    }

    public static LinuxProcMemReader? TryCreate(Process process, Action<SteamHookDiagnostic>? diagnosticListener)
    {
        var path = $"/proc/{process.Id}/mem";
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new LinuxProcMemReader(stream);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            diagnosticListener?.Invoke(
                SteamHookDiagnostic.Create(
                    "steam_hook_linux_memory_read_denied",
                    new Dictionary<string, string>
                    {
                        ["path"] = path,
                        ["exception"] = ex.GetType().Name,
                    }));

            return null;
        }
    }

    public bool TryReadMemory(nuint address, int readLength, out ReadOnlyMemory<byte> data)
    {
        var buffer = new byte[Math.Clamp(readLength, 32, 16 * 1024)];
        try
        {
            _stream.Seek((long)address, SeekOrigin.Begin);
            var bytesRead = _stream.Read(buffer, 0, buffer.Length);
            if (bytesRead <= 0)
            {
                data = ReadOnlyMemory<byte>.Empty;
                return false;
            }

            data = buffer.AsMemory(0, bytesRead);
            return true;
        }
        catch (IOException)
        {
            data = ReadOnlyMemory<byte>.Empty;
            return false;
        }
    }

    public void Dispose() => _stream.Dispose();
}
