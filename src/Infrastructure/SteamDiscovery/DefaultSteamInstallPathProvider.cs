namespace SteamDiscovery;

public sealed class DefaultSteamInstallPathProvider : ISteamInstallPathProvider
{
    private readonly IPlatformProvider _platformProvider;
    private readonly IWindowsSteamInstallPathProvider _windowsProvider;
    private readonly ILinuxSteamInstallPathProvider _linuxProvider;

    public DefaultSteamInstallPathProvider(
        IPlatformProvider platformProvider,
        IWindowsSteamInstallPathProvider windowsProvider,
        ILinuxSteamInstallPathProvider linuxProvider
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _windowsProvider = windowsProvider ?? throw new ArgumentNullException(nameof(windowsProvider));
        _linuxProvider = linuxProvider ?? throw new ArgumentNullException(nameof(linuxProvider));
    }

    public string? GetSteamInstallPath()
    {
        if (_platformProvider.IsWindows())
        {
            return _windowsProvider.GetSteamInstallPath();
        }

        if (_platformProvider.IsLinux())
        {
            return _linuxProvider.GetSteamInstallPath();
        }

        return null;
    }
}
