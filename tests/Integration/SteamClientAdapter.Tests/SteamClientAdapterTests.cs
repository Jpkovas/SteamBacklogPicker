using System;
using FluentAssertions;
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

    [Fact]
    public void Initialize_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        var mocks = new SteamApiMocks(Array.Empty<uint>());
        var adapter = CreateAdapter(mocks);
        adapter.Dispose();

        // Act
        var act = () => adapter.Initialize("steam_api64.dll");

        // Assert
        act.Should().Throw<ObjectDisposedException>()
            .WithMessage("*SteamClientAdapter*");
    }

    [Fact]
    public void GetInstalledAppIds_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        var mocks = new SteamApiMocks(new uint[] { 10, 20 });
        var adapter = CreateAdapter(mocks);
        adapter.Initialize("steam_api64.dll");
        adapter.Dispose();

        // Act
        var act = () => adapter.GetInstalledAppIds();

        // Assert
        act.Should().Throw<ObjectDisposedException>()
            .WithMessage("*SteamClientAdapter*");
    }

    [Fact]
    public void IsSubscribedFromFamilySharing_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        var mocks = new SteamApiMocks(new uint[] { 10 });
        var adapter = CreateAdapter(mocks);
        adapter.Initialize("steam_api64.dll");
        adapter.Dispose();

        // Act
        var act = () => adapter.IsSubscribedFromFamilySharing(10);

        // Assert
        act.Should().Throw<ObjectDisposedException>()
            .WithMessage("*SteamClientAdapter*");
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var mocks = new SteamApiMocks(Array.Empty<uint>());
        var adapter = CreateAdapter(mocks);
        adapter.Initialize("steam_api64.dll");

        // Act
        var act = () =>
        {
            adapter.Dispose();
            adapter.Dispose();
            adapter.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_WithoutInitialization_DoesNotThrow()
    {
        // Arrange
        var mocks = new SteamApiMocks(Array.Empty<uint>());
        var adapter = CreateAdapter(mocks);

        // Act
        var act = () => adapter.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Initialize_WhenAlreadyInitialized_ReturnsTrue()
    {
        // Arrange
        var mocks = new SteamApiMocks(new uint[] { 10 });
        var adapter = CreateAdapter(mocks);
        adapter.Initialize("steam_api64.dll").Should().BeTrue();

        // Act
        var result = adapter.Initialize("steam_api64.dll");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Initialize_WhenAlreadyInitialized_DoesNotReinitialize()
    {
        // Arrange
        var mocks = new SteamApiMocks(new uint[] { 10 });
        var adapter = CreateAdapter(mocks);
        adapter.Initialize("steam_api64.dll");
        var firstCallCount = 0;
        var originalInitResult = mocks.SteamApi.InitResult;

        // Track if Init is called again
        var initCallCount = 0;
        var originalInit = mocks.SteamApi.InitResult;
        mocks.SteamApi.InitResult = false; // Change to false to detect if Init is called again

        // Act
        var result = adapter.Initialize("steam_api64.dll");

        // Assert
        result.Should().BeTrue("already initialized adapters should return true without re-initializing");
        // If Init were called again, it would return false, but we should still get true
    }

    [Fact]
    public void GetInstalledAppIds_BeforeInitialization_UsesFallback()
    {
        // Arrange
        var fallbackAppIds = new uint[] { 100, 200 };
        var mocks = new SteamApiMocks(fallbackAppIds);
        var adapter = CreateAdapter(mocks);

        // Act
        var appIds = adapter.GetInstalledAppIds();

        // Assert
        appIds.Should().BeEquivalentTo(fallbackAppIds);
    }

    [Fact]
    public void IsSubscribedFromFamilySharing_BeforeInitialization_UsesFallback()
    {
        // Arrange
        var mocks = new SteamApiMocks(new uint[] { 50 });
        mocks.Fallback.FamilySharingPredicate = appId => appId == 50;
        var adapter = CreateAdapter(mocks);

        // Act
        var result = adapter.IsSubscribedFromFamilySharing(50);

        // Assert
        result.Should().BeTrue("fallback should be used when not initialized");
    }
}
