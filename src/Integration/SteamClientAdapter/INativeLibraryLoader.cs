using System;
using System.Runtime.InteropServices;

namespace SteamClientAdapter;

public interface INativeLibraryLoader
{
    IntPtr Load(string path);

    T GetDelegate<T>(IntPtr handle, string export) where T : Delegate;

    void Free(IntPtr handle);
}

public sealed class DefaultNativeLibraryLoader : INativeLibraryLoader
{
    public IntPtr Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Library path must be provided.", nameof(path));
        }

        return NativeLibrary.Load(path);
    }

    public T GetDelegate<T>(IntPtr handle, string export) where T : Delegate
    {
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException("Invalid library handle.", nameof(handle));
        }

        if (string.IsNullOrWhiteSpace(export))
        {
            throw new ArgumentException("Export name must be provided.", nameof(export));
        }

        var address = NativeLibrary.GetExport(handle, export);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    public void Free(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            NativeLibrary.Free(handle);
        }
    }
}
