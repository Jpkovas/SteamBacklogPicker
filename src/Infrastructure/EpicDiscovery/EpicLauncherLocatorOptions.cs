using System.Collections.Generic;

namespace EpicDiscovery;

public sealed class EpicLauncherLocatorOptions
{
    public string? ManifestsDirectoryOverride { get; set; }

    public string? CatalogCacheDirectoryOverride { get; set; }

    public IReadOnlyCollection<string> AdditionalManifestDirectories { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> AdditionalCatalogDirectories { get; set; } = Array.Empty<string>();
}
