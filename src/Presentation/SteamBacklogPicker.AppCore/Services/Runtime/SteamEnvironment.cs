using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Infrastructure.Telemetry;
using SteamClientAdapter;
using SteamDiscovery;

namespace SteamBacklogPicker.UI.Services.Runtime;

/// <summary>
/// Provides runtime facilities for resolving the Steam installation directory and initializing the native client adapter.
/// </summary>
public sealed class SteamEnvironment : ISteamEnvironment
{
    private readonly ISteamInstallPathProvider _installPathProvider;
    private readonly ITelemetryClient? _telemetryClient;
    private readonly Lazy<string> _steamDirectory;

    public SteamEnvironment(ISteamInstallPathProvider installPathProvider, ITelemetryClient? telemetryClient = null)
    {
        _installPathProvider = installPathProvider ?? throw new ArgumentNullException(nameof(installPathProvider));
        _telemetryClient = telemetryClient;
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
            _telemetryClient?.TrackEvent("steam_native_library_directory_missing", new Dictionary<string, object>
            {
                ["platform"] = GetPlatformName()
            });
            return;
        }

        var candidateLibraries = SteamNativeLibraryCandidates.Build(directory, GetCurrentPlatform());
        var existingCandidates = new List<string>();

        foreach (var candidate in candidateLibraries)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            existingCandidates.Add(candidate);
            if (adapter.Initialize(candidate))
            {
                _telemetryClient?.TrackEvent("steam_native_library_loaded", new Dictionary<string, object>
                {
                    ["platform"] = GetPlatformName(),
                    ["libraryPath"] = candidate
                });
                return;
            }
        }

        if (existingCandidates.Count == 0)
        {
            _telemetryClient?.TrackEvent("steam_native_library_not_found", new Dictionary<string, object>
            {
                ["platform"] = GetPlatformName(),
                ["candidateCount"] = candidateLibraries.Count
            });
            return;
        }

        _telemetryClient?.TrackEvent("steam_native_library_load_failed", new Dictionary<string, object>
        {
            ["platform"] = GetPlatformName(),
            ["attemptedCandidates"] = string.Join(";", existingCandidates),
            ["attemptedCount"] = existingCandidates.Count
        });
    }

    private static OSPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OSPlatform.Linux;
        }

        return OSPlatform.Windows;
    }

    private static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        return "Windows";
    }

    private string ResolveSteamDirectory()
    {
        var installPath = _installPathProvider.GetSteamInstallPath();
        return !string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath)
            ? installPath
            : string.Empty;
    }
}
