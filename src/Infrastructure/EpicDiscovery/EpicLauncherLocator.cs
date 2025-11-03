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

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }
}
