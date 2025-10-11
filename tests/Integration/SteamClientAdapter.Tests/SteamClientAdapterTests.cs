using System;
using SteamClientAdapter;
using SteamTestUtilities.Steamworks;
using Xunit;
using SteamClientAdapterLib = SteamClientAdapter.SteamClientAdapter;

namespace SteamClientAdapter.Tests;

public sealed class SteamClientAdapterTests
{
    private static SteamClientAdapterLib CreateAdapter(SteamApiMocks mocks)
    {
        return new SteamClientAdapterLib(mocks.Loader, mocks.Fallback);
    }

    [Fact]
    public void InitializeAndQuery_UsesNativeFunctions()
    {
        var mocks = new SteamApiMocks(new uint[] { 10, 20, 30 });
        var adapter = CreateAdapter(mocks);

        mocks.SteamApi.AppInstallationPredicate = (_, appId) => appId is 10 or 20;
        mocks.SteamApi.FamilySharingPredicate = (_, appId) => appId == 20;

        Assert.True(adapter.Initialize("steam_api64.dll"));

        var appIds = adapter.GetInstalledAppIds();

        Assert.Equal(new uint[] { 10, 20 }, appIds);
        Assert.True(adapter.IsSubscribedFromFamilySharing(20));
        Assert.False(adapter.IsSubscribedFromFamilySharing(10));
    }

    [Fact]
    public void InitializeFails_FallbackUsed()
    {
        var mocks = new SteamApiMocks(new uint[] { 5 });
        var adapter = CreateAdapter(mocks);

        mocks.SteamApi.InitResult = false;

        Assert.False(adapter.Initialize("steam_api64.dll"));
        var appIds = adapter.GetInstalledAppIds();
        Assert.Equal(new uint[] { 5 }, appIds);
    }

    [Fact]
    public void Dispose_ShutsDownNative()
    {
        var mocks = new SteamApiMocks(Array.Empty<uint>());
        var adapter = CreateAdapter(mocks);

        Assert.True(adapter.Initialize("steam_api64.dll"));
        adapter.Dispose();

        Assert.True(mocks.SteamApi.ShutdownCalled);
    }
}
