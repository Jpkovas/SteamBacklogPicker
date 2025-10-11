using System.IO;

namespace SteamClientAdapter;

public interface IFileAccessor
{
    bool FileExists(string path);

    string ReadAllText(string path);

    Stream OpenRead(string path);
}

public sealed class DefaultFileAccessor : IFileAccessor
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public Stream OpenRead(string path) => File.OpenRead(path);
}
