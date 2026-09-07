using System.IO;

namespace SteamClientAdapter;

public interface IFileAccessor
{
    bool FileExists(string path);

    // A null version disables snapshot reuse for virtual/unknown file systems.
    string? GetFileVersion(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}:{file.CreationTimeUtc.Ticks}" : "missing";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    IReadOnlyList<string> EnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    string ReadAllText(string path);

    Stream OpenRead(string path);

    void CreateDirectory(string path);

    void WriteAllBytes(string path, byte[] contents);
}

public sealed class DefaultFileAccessor : IFileAccessor
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllBytes(string path, byte[] contents) => File.WriteAllBytes(path, contents);
}
