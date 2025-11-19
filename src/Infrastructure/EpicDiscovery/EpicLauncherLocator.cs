using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;

namespace EpicDiscovery;

public sealed class EpicLauncherLocator : IEpicLauncherLocator
{
    private readonly EpicLauncherLocatorOptions options;

    public EpicLauncherLocator(IOptions<EpicLauncherLocatorOptions> options)
    {
        this.options = options?.Value ?? new EpicLauncherLocatorOptions();
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }

    private static string? GetRegistryInstallLocation()
    {
        if (System.OperatingSystem.IsWindows())
        {
            try
            {
                // Try 64-bit view first, then 32-bit
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher");
                if (key?.GetValue("AppDataPath") is string path && Directory.Exists(path))
                {
                    return path;
                }

                using var key32 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher");
                if (key32?.GetValue("AppDataPath") is string path32 && Directory.Exists(path32))
                {
                    return path32;
                }

                // Try Current User
                using var keyUser = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\EpicGamesLauncher");
                if (keyUser?.GetValue("AppDataPath") is string pathUser && Directory.Exists(pathUser))
                {
                    return pathUser;
                }
            }
            catch
            {
                // Ignore registry access errors
            }
        }
        return null;
    }

    public string? GetLauncherInstalledDatPath()
    {
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(commonAppData))
        {
            var path = Path.Combine(commonAppData, "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    public IReadOnlyCollection<string> GetManifestDirectories()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.ManifestsDirectoryOverride))
        {
            directories.Add(NormalizePath(options.ManifestsDirectoryOverride!));
        }
        else
        {
            var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(commonAppData))
            {
                directories.Add(NormalizePath(Path.Combine(commonAppData, "Epic", "EpicGamesLauncher", "Data", "Manifests")));
            }

            var registryPath = GetRegistryInstallLocation();
            if (!string.IsNullOrWhiteSpace(registryPath))
            {
                directories.Add(NormalizePath(Path.Combine(registryPath, "Manifests")));
            }
        }

        foreach (var additional in options.AdditionalManifestDirectories ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(additional))
            {
                directories.Add(NormalizePath(additional));
            }
        }

        return directories.Where(Directory.Exists).ToArray();
    }

    public IReadOnlyCollection<string> GetCatalogDirectories()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.CatalogCacheDirectoryOverride))
        {
            directories.Add(NormalizePath(options.CatalogCacheDirectoryOverride!));
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                directories.Add(NormalizePath(Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Data", "Catalog")));
            }

            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(roaming))
            {
                directories.Add(NormalizePath(Path.Combine(roaming, "Epic", "EpicGamesLauncher", "Saved", "Data", "Catalog")));
            }

            var registryPath = GetRegistryInstallLocation();
            if (!string.IsNullOrWhiteSpace(registryPath))
            {
                // Registry path points to "Epic Games\EpicGamesLauncher" usually.
                // Catalog is often in ProgramData or AppData, but if installed elsewhere, check relative paths?
                // Actually, the registry key "AppDataPath" usually points to the Data folder (e.g. C:\ProgramData\Epic\EpicGamesLauncher\Data).
                // But Catalog is in ...\Saved\Data\Catalog usually?
                // Let's check the structure.
                // Default ProgramData: C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests
                // Default Catalog: C:\ProgramData\Epic\EpicGamesLauncher\Data\Catalog (Wait, locator says LocalAppData...)
                
                // Locator existing logic:
                // LocalAppData: ...\EpicGamesLauncher\Saved\Data\Catalog
                // Roaming: ...\Epic\EpicGamesLauncher\Saved\Data\Catalog
                
                // If "AppDataPath" from registry points to "C:\ProgramData\Epic\EpicGamesLauncher\Data",
                // Then maybe Catalog is nearby?
                // Let's assume standard structure relative to the InstallLocation if possible, but AppDataPath is specific.
                
                // If we look at the search results:
                // Manifests: %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests
                // Catalog: %LocalAppdata%\EpicGamesLauncher\Saved\Data\Catalog
                
                // The registry "AppDataPath" might point to the Config/Data root.
                // Let's try to add a path based on it just in case.
                // If AppDataPath = ...\Data
                // Maybe ...\..\Saved\Data\Catalog ? 
                // Or just ...\Catalog ?
                
                // To be safe, let's look for "Saved\Data\Catalog" relative to the parent of Data if it looks like that.
                // But actually, Catalog is usually in LocalAppData, not ProgramData.
                // However, if the user moved the data, maybe it's different.
                
                // Let's just add the Registry path + "Catalog" and "Saved\Data\Catalog" just in case.
                
                directories.Add(NormalizePath(Path.Combine(registryPath, "Catalog")));
                directories.Add(NormalizePath(Path.Combine(registryPath, "..", "Saved", "Data", "Catalog")));
            }
        }

        foreach (var additional in options.AdditionalCatalogDirectories ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(additional))
            {
                directories.Add(NormalizePath(additional));
            }
        }

        return directories.Where(Directory.Exists).ToArray();
    }
}
