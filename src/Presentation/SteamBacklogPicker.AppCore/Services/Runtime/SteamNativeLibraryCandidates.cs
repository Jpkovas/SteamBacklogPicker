using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SteamBacklogPicker.UI.Services.Runtime;

/// <summary>
/// Builds candidate native Steam API library paths for each supported runtime platform.
/// </summary>
public static class SteamNativeLibraryCandidates
{
    public static IReadOnlyList<string> Build(string steamDirectory, OSPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(steamDirectory))
        {
            throw new ArgumentException("Steam directory cannot be null or whitespace.", nameof(steamDirectory));
        }

        var normalizedRoot = steamDirectory.Trim();
        var candidates = new List<string>();

        if (platform == OSPlatform.Linux)
        {
            AddLinuxCandidates(candidates, normalizedRoot);
            return candidates;
        }

        AddWindowsCandidates(candidates, normalizedRoot);
        return candidates;
    }

    private static void AddWindowsCandidates(ICollection<string> candidates, string steamDirectory)
    {
        // Keep explicit Windows fallback order unchanged.
        candidates.Add(Path.Combine(steamDirectory, "steamclient.dll"));
        candidates.Add(Path.Combine(steamDirectory, "steam_api64.dll"));
        candidates.Add(Path.Combine(steamDirectory, "steam_api.dll"));
    }

    private static void AddLinuxCandidates(ICollection<string> candidates, string steamDirectory)
    {
        candidates.Add(Path.Combine(steamDirectory, "libsteam_api.so"));
        candidates.Add(Path.Combine(steamDirectory, "steamclient.so"));

        var commonRuntimeSubdirectories = new[]
        {
            "linux64",
            "linux32",
            "ubuntu12_64",
            "ubuntu12_32",
            Path.Combine("steamapps", "common", "SteamLinuxRuntime_sniper"),
            Path.Combine("steamapps", "common", "SteamLinuxRuntime_soldier")
        };

        foreach (var subdirectory in commonRuntimeSubdirectories)
        {
            candidates.Add(Path.Combine(steamDirectory, subdirectory, "libsteam_api.so"));
            candidates.Add(Path.Combine(steamDirectory, subdirectory, "steamclient.so"));
        }
    }
}
