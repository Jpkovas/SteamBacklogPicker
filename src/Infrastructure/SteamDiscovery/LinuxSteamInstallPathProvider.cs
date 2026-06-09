using System.IO;

namespace SteamDiscovery;

public sealed class LinuxSteamInstallPathProvider : ILinuxSteamInstallPathProvider
{
    private readonly IEnvironmentProvider _environmentProvider;
    private readonly IFileSystem _fileSystem;

    public LinuxSteamInstallPathProvider(IEnvironmentProvider environmentProvider, IFileSystem fileSystem)
    {
        _environmentProvider = environmentProvider ?? throw new ArgumentNullException(nameof(environmentProvider));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public string? GetSteamInstallPath()
    {
        // Ordem de resolução Linux:
        // 1) STEAM_PATH explícito
        // 2) XDG_DATA_HOME/Steam, quando configurado
        // 3) caminhos tradicionais (~/.steam/steam, ~/.steam/debian-installation, ~/.local/share/Steam)
        // 4) empacotamentos isolados (Flatpak, Snap)
        // Cada candidato só é aceito quando contém steamapps/libraryfolders.vdf.
        var fromEnvironment = _environmentProvider.GetEnvironmentVariable("STEAM_PATH");
        if (IsValidSteamDirectory(fromEnvironment))
        {
            return fromEnvironment;
        }

        var homeDirectory = _environmentProvider.GetHomeDirectory();
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return null;
        }

        var candidates = GetCandidatePaths(homeDirectory);

        foreach (var candidate in candidates)
        {
            if (IsValidSteamDirectory(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> GetCandidatePaths(string homeDirectory)
    {
        var xdgDataHome = _environmentProvider.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            yield return Path.Combine(xdgDataHome, "Steam");
        }

        yield return Path.Combine(homeDirectory, ".steam", "steam");
        yield return Path.Combine(homeDirectory, ".steam", "debian-installation");
        yield return Path.Combine(homeDirectory, ".local", "share", "Steam");
        yield return Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        yield return Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
        yield return Path.Combine(homeDirectory, "snap", "steam", "common", ".local", "share", "Steam");
    }

    private bool IsValidSteamDirectory(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !_fileSystem.DirectoryExists(candidate))
        {
            return false;
        }

        return _fileSystem.FileExists(Path.Combine(candidate, "steamapps", "libraryfolders.vdf"));
    }
}
