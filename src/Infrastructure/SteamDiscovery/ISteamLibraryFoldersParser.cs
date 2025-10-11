namespace SteamDiscovery;

public interface ISteamLibraryFoldersParser
{
    IReadOnlyList<string> Parse(string content);
}
