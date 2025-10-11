using System;
using System.Collections.Generic;
using SteamClientAdapter;
using Xunit;

namespace SteamClientAdapter.Tests;

public sealed class SteamClientAdapterTests
{
    [Fact]
    public void InitializeAndQuery_UsesNativeFunctions()
    {
        var native = new FakeSteamApi();
        var fallback = new FakeFallback(new uint[] { 10, 20, 30 });
        var adapter = new SteamClientAdapter.SteamClientAdapter(new FakeNativeLibraryLoader(native), fallback);

        Assert.True(adapter.Initialize("steam_api64.dll"));

        var appIds = adapter.GetInstalledAppIds();

        Assert.Equal(new uint[] { 10, 20 }, appIds);
        Assert.True(adapter.IsSubscribedFromFamilySharing(20));
        Assert.False(adapter.IsSubscribedFromFamilySharing(10));
    }

    [Fact]
    public void InitializeFails_FallbackUsed()
    {
        var native = new FakeSteamApi { InitResult = false };
        var fallback = new FakeFallback(new uint[] { 5 });
        var adapter = new SteamClientAdapter.SteamClientAdapter(new FakeNativeLibraryLoader(native), fallback);

        Assert.False(adapter.Initialize("steam_api64.dll"));
        var appIds = adapter.GetInstalledAppIds();
        Assert.Equal(new uint[] { 5 }, appIds);
    }

    [Fact]
    public void Dispose_ShutsDownNative()
    {
        var native = new FakeSteamApi();
        var fallback = new FakeFallback(Array.Empty<uint>());
        var adapter = new SteamClientAdapter.SteamClientAdapter(new FakeNativeLibraryLoader(native), fallback);

        Assert.True(adapter.Initialize("steam_api64.dll"));
        adapter.Dispose();

        Assert.True(native.ShutdownCalled);
    }

    private sealed class FakeNativeLibraryLoader : INativeLibraryLoader
    {
        private readonly FakeSteamApi _api;

        public FakeNativeLibraryLoader(FakeSteamApi api)
        {
            _api = api;
        }

        public void Free(IntPtr handle)
        {
        }

        public T GetDelegate<T>(IntPtr handle, string export) where T : Delegate
        {
            return export switch
            {
                "SteamAPI_Init" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_InitDelegate(_api.Init),
                "SteamAPI_Shutdown" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_ShutdownDelegate(_api.Shutdown),
                "SteamAPI_SteamApps" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_SteamAppsDelegate(_api.GetSteamApps),
                "SteamAPI_ISteamApps_BIsAppInstalled" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_ISteamApps_BIsAppInstalledDelegate(_api.IsAppInstalled),
                "SteamAPI_ISteamApps_BIsSubscribedFromFamilySharing" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_ISteamApps_BIsSubscribedFromFamilySharingDelegate(_api.IsFamilyShared),
                _ => throw new InvalidOperationException(export),
            };
        }

        public IntPtr Load(string path) => new IntPtr(1);
    }

    private sealed class FakeSteamApi
    {
        public bool InitResult { get; set; } = true;

        public bool ShutdownCalled { get; private set; }

        public IntPtr Handle { get; } = new(42);

        public bool Init() => InitResult;

        public void Shutdown() => ShutdownCalled = true;

        public IntPtr GetSteamApps() => Handle;

        public bool IsAppInstalled(IntPtr self, uint appId) => self == Handle && appId is 10 or 20;

        public bool IsFamilyShared(IntPtr self, uint appId) => self == Handle && appId == 20;
    }

    private sealed class FakeFallback : ISteamVdfFallback
    {
        private readonly IReadOnlyCollection<uint> _appIds;

        public FakeFallback(IReadOnlyCollection<uint> appIds)
        {
            _appIds = appIds;
        }

        public IReadOnlyCollection<uint> GetInstalledAppIds() => _appIds;

        public bool IsSubscribedFromFamilySharing(uint appId) => false;
    }
}
