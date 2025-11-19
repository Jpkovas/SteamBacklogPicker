using System.IO;

namespace SteamClientAdapter;

public interface IFileAccessor
{
    bool FileExists(string path);

    string ReadAllText(string path);

    Stream OpenRead(string path);

    void CreateDirectory(string path);

    void WriteAllBytes(string path, byte[] contents);
}

public sealed class DefaultFileAccessor : IFileAccessor
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public Stream OpenRead(string path) => File.OpenRead(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllBytes(string path, byte[] contents) => File.WriteAllBytes(path, contents);
}
