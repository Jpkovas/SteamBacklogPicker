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
        // 2) caminhos tradicionais (~/.steam/steam, ~/.steam/debian-installation, ~/.local/share/Steam)
        // 3) empacotamentos isolados (Flatpak, Snap)
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

        var candidates = new[]
        {
            Path.Combine(homeDirectory, ".steam", "steam"),
            Path.Combine(homeDirectory, ".steam", "debian-installation"),
            Path.Combine(homeDirectory, ".local", "share", "Steam"),
            Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
            Path.Combine(homeDirectory, "snap", "steam", "common", ".local", "share", "Steam")
        };

        foreach (var candidate in candidates)
        {
            if (IsValidSteamDirectory(candidate))
            {
                return candidate;
            }
        }

        return null;
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
