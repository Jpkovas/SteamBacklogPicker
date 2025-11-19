using System.Collections.Generic;
using System.Collections.Generic;

namespace EpicDiscovery;

public interface IEpicLauncherLocator
{
    IReadOnlyCollection<string> GetManifestDirectories();

    IReadOnlyCollection<string> GetCatalogDirectories();
    string? GetLauncherInstalledDatPath();
}
