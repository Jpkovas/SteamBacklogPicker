using System.Runtime.InteropServices;

namespace SteamDiscovery;

public interface IEnvironmentProvider
{
    string? GetEnvironmentVariable(string variable);

    string? GetHomeDirectory();
}

public interface IFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);
}

public interface IPlatformProvider
{
    bool IsWindows();

    bool IsLinux();
}

public sealed class SystemEnvironmentProvider : IEnvironmentProvider
{
    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);

    public string? GetHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? null : home;
    }
}

public sealed class SystemFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);
}

public sealed class RuntimePlatformProvider : IPlatformProvider
{
    public bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}
