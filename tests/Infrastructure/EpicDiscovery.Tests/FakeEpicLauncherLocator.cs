using System.Collections.Generic;
using EpicDiscovery;

namespace EpicDiscovery.Tests;

internal sealed class FakeEpicLauncherLocator : IEpicLauncherLocator
{
    private readonly IReadOnlyCollection<string> manifestDirectories;
    private readonly IReadOnlyCollection<string> catalogDirectories;

    public FakeEpicLauncherLocator(
        IReadOnlyCollection<string>? manifestDirectories = null,
        IReadOnlyCollection<string>? catalogDirectories = null)
    {
        this.manifestDirectories = manifestDirectories ?? new List<string>();
        this.catalogDirectories = catalogDirectories ?? new List<string>();
    }

    public IReadOnlyCollection<string> GetManifestDirectories()
    {
        return manifestDirectories;
    }

    public IReadOnlyCollection<string> GetCatalogDirectories()
    {
        return catalogDirectories;
    }

    public string? GetLauncherInstalledDatPath()
    {
        return null;
    }
}
