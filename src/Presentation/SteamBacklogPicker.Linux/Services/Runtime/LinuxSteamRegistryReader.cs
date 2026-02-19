using System;
using System.IO;
using SteamDiscovery;

namespace SteamBacklogPicker.Linux.Services.Runtime;

public sealed class LinuxSteamRegistryReader : ISteamRegistryReader
{
    public string? GetSteamInstallPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("STEAM_PATH");
        if (IsValidSteamDirectory(fromEnvironment))
        {
            return fromEnvironment;
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(homeDirectory, ".local", "share", "Steam"),
            Path.Combine(homeDirectory, ".steam", "steam"),
            Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
        };

        foreach (var candidate in candidates)
        {
            if (IsValidSteamDirectory(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsValidSteamDirectory(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
        {
            return false;
        }

        return File.Exists(Path.Combine(candidate, "steamapps", "libraryfolders.vdf"));
    }
}
