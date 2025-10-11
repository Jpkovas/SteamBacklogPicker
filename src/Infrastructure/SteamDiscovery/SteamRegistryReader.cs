using System.IO;
using Microsoft.Win32;

namespace SteamDiscovery;

public sealed class SteamRegistryReader : ISteamRegistryReader
{
    private const string SteamKeyPath = @"Software\\Valve\\Steam";
    private const string SteamPathValueName = "SteamPath";

    public string? GetSteamInstallPath()
    {
        try
        {
            return TryGetSteamPath(RegistryView.Registry64)
                   ?? TryGetSteamPath(RegistryView.Registry32);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? TryGetSteamPath(RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            using var steamKey = baseKey.OpenSubKey(SteamKeyPath, writable: false);
            return steamKey?.GetValue(SteamPathValueName) as string;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
