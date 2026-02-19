namespace SteamDiscovery;

public interface ISteamInstallPathProvider
{
    string? GetSteamInstallPath();
}

public interface IWindowsSteamInstallPathProvider : ISteamInstallPathProvider
{
}

public interface ILinuxSteamInstallPathProvider : ISteamInstallPathProvider
{
}
