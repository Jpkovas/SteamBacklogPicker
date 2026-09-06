using ValveFormatParser;

namespace SteamDiscovery;

public sealed class SteamLibraryFoldersParser : ISteamLibraryFoldersParser
{
    private readonly IPathComparisonStrategy _pathComparison;

    public SteamLibraryFoldersParser()
        : this(new PlatformPathComparisonStrategy(new RuntimePlatformProvider())) { }

    public SteamLibraryFoldersParser(IPathComparisonStrategy pathComparison)
    {
        _pathComparison = pathComparison ?? throw new ArgumentNullException(nameof(pathComparison));
    }

    public IReadOnlyList<string> Parse(string content)
    {
        return ExtractPaths(new ValveTextVdfParser().Parse(content));
    }

    public IReadOnlyList<string> Parse(Stream stream)
    {
        return ExtractPaths(new ValveTextVdfParser().Parse(stream));
    }

    private IReadOnlyList<string> ExtractPaths(ValveKeyValueNode root)
    {
        var folders = root.Children.Values.FirstOrDefault(node =>
            node.Name.Equals("LibraryFolders", StringComparison.OrdinalIgnoreCase));
        if (folders is null) return Array.Empty<string>();
        var paths = new List<string>();
        foreach (var (key, value) in folders.Children)
        {
            if (int.TryParse(key, out _))
            {
                if (!value.IsObject && value.Value is { } legacyPath) paths.Add(legacyPath);
                else
                {
                    var path = value.Children.Values.FirstOrDefault(node => node.Name.Equals("path", StringComparison.OrdinalIgnoreCase))
                        ?? value.Children.Values.FirstOrDefault(node => node.Name.Equals("contentpath", StringComparison.OrdinalIgnoreCase));
                    if (path?.Value is { } nestedPath) paths.Add(nestedPath);
                }
            }
            else if ((key.Equals("path", StringComparison.OrdinalIgnoreCase) || key.Equals("contentpath", StringComparison.OrdinalIgnoreCase)) && value.Value is { } path)
            {
                paths.Add(path);
            }
        }
        return paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim())
            .Distinct(_pathComparison.Comparer).ToArray();
    }
}
