using System.Collections.Generic;
using EpicDiscovery;

namespace EpicDiscovery.Tests;

internal sealed class FakeEpicLauncherLocator : IEpicLauncherLocator
{
    private readonly IReadOnlyCollection<string> manifestDirectories;
    private readonly IReadOnlyCollection<string> catalogDirectories;
    private readonly string? launcherInstalledDatPath;

    public FakeEpicLauncherLocator(
        IReadOnlyCollection<string>? manifestDirectories = null,
        IReadOnlyCollection<string>? catalogDirectories = null,
        string? launcherInstalledDatPath = null)
    {
        this.manifestDirectories = manifestDirectories ?? new List<string>();
        this.catalogDirectories = catalogDirectories ?? new List<string>();
        this.launcherInstalledDatPath = launcherInstalledDatPath;
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
        return launcherInstalledDatPath;
    }
}
