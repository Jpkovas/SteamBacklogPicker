using System.IO;
using SteamClientAdapter;

namespace EpicDiscovery.Tests;

internal sealed class TestFileAccessor : IFileAccessor
{
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public string ReadAllText(string path)
    {
        return File.ReadAllText(path);
    }

    public Stream OpenRead(string path)
    {
        return File.OpenRead(path);
    }
}
