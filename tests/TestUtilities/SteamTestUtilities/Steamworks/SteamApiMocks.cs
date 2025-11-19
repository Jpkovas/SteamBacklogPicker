using System;
using System.Collections.Generic;
using System.Linq;
using SteamClientAdapter;

namespace SteamTestUtilities.Steamworks;

public sealed class SteamApiMocks
{
    public SteamApiMocks(IReadOnlyCollection<uint> fallbackAppIds)
    {
        SteamApi = new FakeSteamApi(fallbackAppIds);
        Fallback = new FakeFallback(fallbackAppIds);
        Loader = new FakeNativeLibraryLoader(SteamApi);
    }

    public FakeNativeLibraryLoader Loader { get; }

    public FakeSteamApi SteamApi { get; }

    public FakeFallback Fallback { get; }

    public sealed class FakeNativeLibraryLoader : INativeLibraryLoader
    {
        private readonly FakeSteamApi _api;

        public FakeNativeLibraryLoader(FakeSteamApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public IntPtr Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A Steamworks library path must be provided.", nameof(path));
            }

            return new IntPtr(1);
        }

        public void Free(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                throw new ArgumentException("A valid native handle is required to free resources.", nameof(handle));
            }
        }

        public T GetDelegate<T>(IntPtr handle, string export) where T : Delegate
        {
            if (handle == IntPtr.Zero)
            {
                throw new ArgumentException("A valid native handle is required to resolve exports.", nameof(handle));
            }

            return export switch
            {
                "SteamAPI_Init" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_InitDelegate(_api.Init),
                "SteamAPI_Shutdown" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_ShutdownDelegate(_api.Shutdown),
                "SteamAPI_SteamApps" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_SteamAppsDelegate(_api.GetSteamApps),
                "SteamAPI_ISteamApps_GetInstalledApps" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_ISteamApps_GetInstalledAppsDelegate(_api.GetInstalledApps),
                "SteamAPI_ISteamApps_BIsAppInstalled" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_ISteamApps_BIsAppInstalledDelegate(_api.IsAppInstalled),
                "SteamAPI_ISteamApps_BIsSubscribedFromFamilySharing" => (T)(Delegate)new SteamClientAdapter.SteamClientAdapter.SteamAPI_ISteamApps_BIsSubscribedFromFamilySharingDelegate(_api.IsFamilyShared),
                _ => throw new InvalidOperationException($"Unsupported Steamworks export '{export}'."),
            };
        }
    }

    public sealed class FakeSteamApi
    {
        private readonly List<uint> _installedAppIds;

        public FakeSteamApi(IReadOnlyCollection<uint> installedAppIds)
        {
            _installedAppIds = installedAppIds?.ToList() ?? throw new ArgumentNullException(nameof(installedAppIds));
        }

        public bool InitResult { get; set; } = true;

        public bool ShutdownCalled { get; private set; }

        public IntPtr Handle { get; } = new(42);

        public IReadOnlyCollection<uint> InstalledAppIds => _installedAppIds;

        public bool Init() => InitResult;

        public void Shutdown() => ShutdownCalled = true;

        public IntPtr GetSteamApps() => Handle;

        public Func<IntPtr, uint, bool>? AppInstallationPredicate { get; set; }

        public Func<IntPtr, uint, bool>? FamilySharingPredicate { get; set; }

        public uint GetInstalledApps(IntPtr self, uint[] appIds, uint maxAppIds)
        {
            if (self != Handle)
            {
                return 0;
            }

            var totalInstalled = (uint)_installedAppIds.Count;
            var countToCopy = (int)Math.Min(maxAppIds, totalInstalled);

            for (var index = 0; index < countToCopy; index++)
            {
                appIds[index] = _installedAppIds[index];
            }

            return totalInstalled;
        }

        public void SetInstalledApps(IEnumerable<uint> appIds)
        {
            if (appIds is null)
            {
                throw new ArgumentNullException(nameof(appIds));
            }

            _installedAppIds.Clear();
            _installedAppIds.AddRange(appIds);
        }

        public bool IsAppInstalled(IntPtr self, uint appId)
        {
            if (self != Handle)
            {
                return false;
            }

            return AppInstallationPredicate?.Invoke(self, appId) ?? true;
        }

        public bool IsFamilyShared(IntPtr self, uint appId)
        {
            if (self != Handle)
            {
                return false;
            }

            return FamilySharingPredicate?.Invoke(self, appId) ?? false;
        }
    }

    public sealed class FakeFallback : ISteamVdfFallback
    {
        private readonly Dictionary<uint, SteamAppDefinition> _apps;
        private readonly List<SteamCollectionDefinition> _collections = new();

        public FakeFallback(IReadOnlyCollection<uint> appIds)
        {
            if (appIds is null)
            {
                throw new ArgumentNullException(nameof(appIds));
            }

            _apps = appIds.ToDictionary(
                id => id,
                id => new SteamAppDefinition(id, $"App {id}", IsInstalled: true, Type: "game", Collections: Array.Empty<string>()));
        }

        public string? CurrentSteamId { get; set; } = "76561198000000000";

        public IReadOnlyCollection<uint> GetInstalledAppIds() => _apps.Values.Where(app => app.IsInstalled).Select(app => app.AppId).ToArray();

        public IReadOnlyDictionary<uint, SteamAppDefinition> GetKnownApps() => _apps;

        public IReadOnlyList<SteamCollectionDefinition> GetCollections() => _collections;

        public void SetAppDefinition(SteamAppDefinition definition)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            _apps[definition.AppId] = definition;
        }

        public Func<uint, bool>? FamilySharingPredicate { get; set; }

        public bool IsSubscribedFromFamilySharing(uint appId) => FamilySharingPredicate?.Invoke(appId) ?? false;

        public string? GetCurrentUserSteamId() => CurrentSteamId;
    }
}
