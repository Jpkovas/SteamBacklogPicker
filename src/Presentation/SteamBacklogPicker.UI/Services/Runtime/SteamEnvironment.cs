using System;
using System.Collections.Generic;
using System.IO;
using SteamClientAdapter;
using SteamDiscovery;

namespace SteamBacklogPicker.UI.Services.Runtime;

/// <summary>
/// Provides runtime facilities for resolving the Steam installation directory and initializing the native client adapter.
/// </summary>
public sealed class SteamEnvironment : ISteamEnvironment
{
    private readonly ISteamInstallPathProvider _installPathProvider;
    private readonly Lazy<string> _steamDirectory;

    public SteamEnvironment(ISteamInstallPathProvider installPathProvider)
    {
        _installPathProvider = installPathProvider ?? throw new ArgumentNullException(nameof(installPathProvider));
        _steamDirectory = new Lazy<string>(ResolveSteamDirectory, isThreadSafe: true);
    }

    public string GetSteamDirectory() => _steamDirectory.Value;

    public void TryInitializeSteamApi(ISteamClientAdapter adapter)
    {
        if (adapter is null)
        {
            throw new ArgumentNullException(nameof(adapter));
        }

        var directory = GetSteamDirectory();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var candidate in GetLibraryCandidates(directory))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (adapter.Initialize(candidate))
            {
                return;
            }
        }
    }

    private IEnumerable<string> GetLibraryCandidates(string directory)
    {
        yield return Path.Combine(directory, "steamclient.dll");
        yield return Path.Combine(directory, "steam_api64.dll");
        yield return Path.Combine(directory, "steam_api.dll");
    }

    private string ResolveSteamDirectory()
    {
        var registryPath = _installPathProvider.GetSteamInstallPath();
        if (!string.IsNullOrWhiteSpace(registryPath) && Directory.Exists(registryPath))
        {
            return registryPath;
        }

        var environmentPath = Environment.GetEnvironmentVariable("STEAM_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath) && Directory.Exists(environmentPath))
        {
            return environmentPath;
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            var candidate = Path.Combine(programFilesX86, "Steam");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var candidate = Path.Combine(localAppData, "Steam");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
