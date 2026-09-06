using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SteamBacklogPicker.Linux.Composition;
using SteamBacklogPicker.UI.Services.Runtime;
using SteamClientAdapter;
using Xunit;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class LinuxBootstrapServiceRegistrationTests
{
    [Fact]
    public void AddLinuxApplicationServices_ShouldResolveAdapterWithoutLoadingSteamGameLibraries()
    {
        var services = new ServiceCollection();
        var environment = new SpySteamEnvironment();

        services.AddLinuxApplicationServices();
        services.Replace(ServiceDescriptor.Singleton<ISteamEnvironment>(environment));

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<ISteamClientAdapter>();

        adapter.Should().NotBeNull();
        environment.TryInitializeCallCount.Should().Be(0);
        environment.ReceivedAdapter.Should().BeNull();
    }

    [Fact]
    public void AddLinuxApplicationServices_ShouldReadLocalVdfWithoutNativeLibraryInitialization()
    {
        var services = new ServiceCollection();
        var environment = new SpySteamEnvironment();
        var fallback = new FakeSteamVdfFallback(new uint[] { 10, 20, 30 });
        var loader = new ThrowingNativeLibraryLoader();

        services.AddLinuxApplicationServices();
        services.Replace(ServiceDescriptor.Singleton<ISteamEnvironment>(environment));
        services.Replace(ServiceDescriptor.Singleton<ISteamVdfFallback>(fallback));
        services.Replace(ServiceDescriptor.Singleton<INativeLibraryLoader>(loader));

        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<ISteamClientAdapter>();

        var installedAppIds = adapter.GetInstalledAppIds();

        environment.TryInitializeCallCount.Should().Be(0);
        loader.LoadCallCount.Should().Be(0);
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

    private sealed class ThrowingNativeLibraryLoader : INativeLibraryLoader
    {
        public int LoadCallCount { get; private set; }

        public IntPtr Load(string path)
        {
            LoadCallCount++;
            throw new DllNotFoundException($"Missing native library: {path}");
        }

        public T GetDelegate<T>(IntPtr handle, string export) where T : Delegate => throw new NotSupportedException();

        public void Free(IntPtr handle)
        {
        }
    }

    private sealed class FakeSteamVdfFallback : ISteamVdfFallback
    {
        private readonly IReadOnlyCollection<uint> _installedAppIds;
        private readonly IReadOnlyDictionary<uint, SteamAppDefinition> _knownApps;

        public FakeSteamVdfFallback(IReadOnlyCollection<uint> installedAppIds)
        {
            _installedAppIds = installedAppIds;
            var knownApps = new Dictionary<uint, SteamAppDefinition>();
            foreach (var appId in installedAppIds)
            {
                knownApps[appId] = new SteamAppDefinition(appId, $"App {appId}", true, "game", Array.Empty<string>())
                {
                    DeckCompatibility = Domain.SteamDeckCompatibility.Unknown,
                    StoreCategoryIds = Array.Empty<int>(),
                };
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

        public IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps() => _knownApps;

        public string? GetCurrentUserSteamId() => null;

        public IReadOnlyList<SteamCollectionDefinition> GetCollections() => Array.Empty<SteamCollectionDefinition>();
    }
}
