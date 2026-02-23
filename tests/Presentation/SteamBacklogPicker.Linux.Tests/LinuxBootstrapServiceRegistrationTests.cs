using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SteamBacklogPicker.Linux.Composition;
using SteamBacklogPicker.UI.Services.Runtime;
using SteamClientAdapter;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class LinuxBootstrapServiceRegistrationTests
{
    [Fact]
    public void AddLinuxApplicationServices_ShouldAttemptToInitializeSteamApi_WhenSteamClientAdapterIsResolved()
    {
        var services = new ServiceCollection();
        var environment = new SpySteamEnvironment();

        services.AddLinuxApplicationServices();
        services.Replace(ServiceDescriptor.Singleton<ISteamEnvironment>(environment));

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<ISteamClientAdapter>();

        environment.TryInitializeCallCount.Should().Be(1);
        environment.ReceivedAdapter.Should().BeSameAs(adapter);
    }

    [Fact]
    public void AddLinuxApplicationServices_ShouldUseVdfFallback_WhenNativeLibraryInitializationFails()
    {
        var services = new ServiceCollection();
        var environment = new FailingInitSteamEnvironment();
        var fallback = new FakeSteamVdfFallback(new uint[] { 10, 20, 30 });

        services.AddLinuxApplicationServices();
        services.Replace(ServiceDescriptor.Singleton<ISteamEnvironment>(environment));
        services.Replace(ServiceDescriptor.Singleton<ISteamVdfFallback>(fallback));
        services.Replace(ServiceDescriptor.Singleton<INativeLibraryLoader>(new ThrowingNativeLibraryLoader()));

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<ISteamClientAdapter>();

        var installedAppIds = adapter.GetInstalledAppIds();

        environment.TryInitializeCallCount.Should().Be(1);
        installedAppIds.Should().BeEquivalentTo(new uint[] { 10, 20, 30 });
        fallback.GetInstalledAppIdsCallCount.Should().BeGreaterThan(0);
    }

    private sealed class SpySteamEnvironment : ISteamEnvironment
    {
        public int TryInitializeCallCount { get; private set; }

        public ISteamClientAdapter? ReceivedAdapter { get; private set; }

        public string GetSteamDirectory() => string.Empty;

        public void TryInitializeSteamApi(ISteamClientAdapter adapter)
        {
            TryInitializeCallCount++;
            ReceivedAdapter = adapter;
        }
    }

    private sealed class FailingInitSteamEnvironment : ISteamEnvironment
    {
        public int TryInitializeCallCount { get; private set; }

        public string GetSteamDirectory() => string.Empty;

        public void TryInitializeSteamApi(ISteamClientAdapter adapter)
        {
            TryInitializeCallCount++;
            adapter.Initialize("/path/that/does/not/exist/libsteam_api.so");
        }
    }

    private sealed class ThrowingNativeLibraryLoader : INativeLibraryLoader
    {
        public IntPtr Load(string path) => throw new DllNotFoundException($"Missing native library: {path}");

        public T GetDelegate<T>(IntPtr handle, string export) where T : Delegate => throw new NotSupportedException();

        public void Free(IntPtr handle)
        {
        }
    }

    private sealed class FakeSteamVdfFallback : ISteamVdfFallback
    {
        private readonly IReadOnlyCollection<uint> _installedAppIds;
        private readonly IReadOnlyDictionary<uint, Domain.SteamAppDefinition> _knownApps;

        public FakeSteamVdfFallback(IReadOnlyCollection<uint> installedAppIds)
        {
            _installedAppIds = installedAppIds;
            var knownApps = new Dictionary<uint, Domain.SteamAppDefinition>();
            foreach (var appId in installedAppIds)
            {
                knownApps[appId] = new Domain.SteamAppDefinition(appId, $"App {appId}", true, false, string.Empty, Array.Empty<int>(), Domain.SteamDeckCompatibility.Unknown, Array.Empty<string>());
            }

            _knownApps = knownApps;
        }

        public int GetInstalledAppIdsCallCount { get; private set; }

        public IReadOnlyCollection<uint> GetInstalledAppIds()
        {
            GetInstalledAppIdsCallCount++;
            return _installedAppIds;
        }

        public bool IsSubscribedFromFamilySharing(uint appId) => false;

        public IReadOnlyDictionary<uint, Domain.SteamAppDefinition> GetKnownApps() => _knownApps;

        public string? GetCurrentUserSteamId() => null;

        public IReadOnlyList<Domain.SteamCollectionDefinition> GetCollections() => Array.Empty<Domain.SteamCollectionDefinition>();
    }
}
