using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteamBacklogPicker.Integration.SteamHooks;

internal sealed class WindowsSteamProcessMemoryReader : ISteamProcessMemoryReader
{
    private readonly SafeProcessHandle _processHandle;

    private WindowsSteamProcessMemoryReader(SafeProcessHandle processHandle)
    {
        _processHandle = processHandle;
    }

    public static WindowsSteamProcessMemoryReader? TryCreate(Process process)
    {
        var access = NativeMethods.ProcessAccessFlags.VirtualMemoryRead | NativeMethods.ProcessAccessFlags.QueryInformation;
        var handle = NativeMethods.OpenProcess(access, false, process.Id);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new WindowsSteamProcessMemoryReader(handle);
    }

    public bool TryReadMemory(nuint address, int readLength, out ReadOnlyMemory<byte> data)
    {
        var buffer = new byte[Math.Clamp(readLength, 32, 16 * 1024)];
        if (!NativeMethods.ReadProcessMemory(_processHandle, (IntPtr)address, buffer, buffer.Length, out var bytesRead) || bytesRead == 0)
        {
            data = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        data = buffer.AsMemory(0, bytesRead);
        return true;
    }

    public void Dispose() => _processHandle.Dispose();

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
}
