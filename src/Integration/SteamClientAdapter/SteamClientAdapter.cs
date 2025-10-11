using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SteamClientAdapter;

public interface ISteamClientAdapter
{
    bool Initialize(string libraryPath);

    IReadOnlyCollection<uint> GetInstalledAppIds();

    bool IsSubscribedFromFamilySharing(uint appId);
}

public sealed class SteamClientAdapter : ISteamClientAdapter, IDisposable
{
    private readonly INativeLibraryLoader _loader;
    private readonly ISteamVdfFallback _fallback;
    private IntPtr _libraryHandle;
    private bool _initialized;
    private bool _disposed;
    private IntPtr _steamAppsPointer;

    private SteamAPI_InitDelegate? _steamApiInit;
    private SteamAPI_ShutdownDelegate? _steamApiShutdown;
    private SteamAPI_SteamAppsDelegate? _steamAppsAccessor;
    private SteamAPI_ISteamApps_BIsAppInstalledDelegate? _isAppInstalled;
    private SteamAPI_ISteamApps_BIsSubscribedFromFamilySharingDelegate? _isSubscribedFromFamilySharing;

    public SteamClientAdapter(INativeLibraryLoader loader, ISteamVdfFallback fallback)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public bool Initialize(string libraryPath)
    {
        if (_initialized)
        {
            return true;
        }

        try
        {
            _libraryHandle = _loader.Load(libraryPath);
            _steamApiInit = _loader.GetDelegate<SteamAPI_InitDelegate>(_libraryHandle, "SteamAPI_Init");
            _steamApiShutdown = _loader.GetDelegate<SteamAPI_ShutdownDelegate>(_libraryHandle, "SteamAPI_Shutdown");
            _steamAppsAccessor = _loader.GetDelegate<SteamAPI_SteamAppsDelegate>(_libraryHandle, "SteamAPI_SteamApps");
            _isAppInstalled = _loader.GetDelegate<SteamAPI_ISteamApps_BIsAppInstalledDelegate>(_libraryHandle, "SteamAPI_ISteamApps_BIsAppInstalled");
            _isSubscribedFromFamilySharing = _loader.GetDelegate<SteamAPI_ISteamApps_BIsSubscribedFromFamilySharingDelegate>(_libraryHandle, "SteamAPI_ISteamApps_BIsSubscribedFromFamilySharing");

            if (_steamApiInit is null || !_steamApiInit())
            {
                Reset();
                return false;
            }

            if (_steamAppsAccessor is null)
            {
                Reset();
                return false;
            }

            _steamAppsPointer = _steamAppsAccessor();
            if (_steamAppsPointer == IntPtr.Zero)
            {
                Reset();
                return false;
            }

            _initialized = true;
            return true;
        }
        catch
        {
            Reset();
            return false;
        }
    }

    public IReadOnlyCollection<uint> GetInstalledAppIds()
    {
        if (_initialized && _isAppInstalled is not null)
        {
            var installed = new List<uint>();
            foreach (var appId in _fallback.GetInstalledAppIds())
            {
                if (_isAppInstalled(_steamAppsPointer, appId))
                {
                    installed.Add(appId);
                }
            }

            if (installed.Count > 0)
            {
                return installed;
            }
        }

        return _fallback.GetInstalledAppIds();
    }

    public bool IsSubscribedFromFamilySharing(uint appId)
    {
        if (_initialized && _isSubscribedFromFamilySharing is not null)
        {
            return _isSubscribedFromFamilySharing(_steamAppsPointer, appId);
        }

        return _fallback.IsSubscribedFromFamilySharing(appId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_initialized)
        {
            _steamApiShutdown?.Invoke();
        }

        Reset();
        GC.SuppressFinalize(this);
    }

    private void Reset()
    {
        if (_initialized)
        {
            _initialized = false;
        }

        if (_libraryHandle != IntPtr.Zero)
        {
            _loader.Free(_libraryHandle);
            _libraryHandle = IntPtr.Zero;
        }

        _steamAppsPointer = IntPtr.Zero;
        _steamApiInit = null;
        _steamApiShutdown = null;
        _steamAppsAccessor = null;
        _isAppInstalled = null;
        _isSubscribedFromFamilySharing = null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool SteamAPI_InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SteamAPI_ShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr SteamAPI_SteamAppsDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool SteamAPI_ISteamApps_BIsAppInstalledDelegate(IntPtr self, uint appId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool SteamAPI_ISteamApps_BIsSubscribedFromFamilySharingDelegate(IntPtr self, uint appId);
}
