using SteamDiscovery;

namespace SteamBacklogPicker.Linux.Services.Runtime;

[Obsolete("Use LinuxSteamInstallPathProvider from SteamDiscovery.")]
public sealed class LinuxSteamRegistryReader : ISteamInstallPathProvider
{
    private readonly LinuxSteamInstallPathProvider _inner;

    public LinuxSteamRegistryReader()
    {
        _inner = new LinuxSteamInstallPathProvider(new SystemEnvironmentProvider(), new SystemFileSystem());
    }

    public string? GetSteamInstallPath() => _inner.GetSteamInstallPath();
}
