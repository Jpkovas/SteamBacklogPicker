using SteamDiscovery;
using Xunit;

namespace SteamDiscovery.Tests;

public sealed class SteamLibraryFoldersParserTests
{
    private readonly SteamLibraryFoldersParser _parser = new();

    [Fact]
    public void Parse_ReturnsEmpty_WhenLibraryFoldersSectionIsMissing()
    {
        const string content = "\"OtherSection\" { \"0\" \"C:\\Steam\" }";

        var result = _parser.Parse(content);

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_ReturnsLegacyPaths()
    {
        const string content = """
"LibraryFolders"
{
    "0" "C:\\Program Files (x86)\\Steam"
    "1" "D:\\SteamLibrary"
}
""";

        var result = _parser.Parse(content);

        Assert.Collection(result,
            path => Assert.Equal("C:\\Program Files (x86)\\Steam", path),
            path => Assert.Equal("D:\\SteamLibrary", path));
    }

    [Fact]
    public void Parse_ReturnsPathsFromObjectNotation()
    {
        const string content = """
"LibraryFolders"
{
    "0"
    {
        "path" "C:\\Steam"
    }
    "1"
    {
        "path" "D:\\Games\\Steam"
        "label" "Secondary"
    }
}
""";

        var result = _parser.Parse(content);

        Assert.Collection(result,
            path => Assert.Equal("C:\\Steam", path),
            path => Assert.Equal("D:\\Games\\Steam", path));
    }

    [Fact]
    public void Parse_UsesContentPathFallback()
    {
        const string content = """
"LibraryFolders"
{
    "2"
    {
        "contentpath" "E:\\SteamContent"
    }
}
""";

        var result = _parser.Parse(content);

        Assert.Single(result);
        Assert.Equal("E:\\SteamContent", result[0]);
    }

    [Fact]
    public void Parse_IgnoresMetadataEntries()
    {
        const string content = """
"LibraryFolders"
{
    "TimeNextStatsReport" "12345"
    "ContentStatsID" "67890"
    "1" "E:\\SteamLibrary"
}
""";

        var result = _parser.Parse(content);

        Assert.Single(result);
        Assert.Equal("E:\\SteamLibrary", result[0]);
    }

    [Fact]
    public void Parse_TrimsAndDeduplicatesPaths()
    {
        const string content = """
"LibraryFolders"
{
    "0" " C:\\Steam "
    "1"
    {
        "path" "C:\\Steam"
    }
    "2" "D:\\SteamLibrary"
    "path" "D:\\SteamLibrary"
}
""";

        var result = _parser.Parse(content);

        Assert.Collection(result,
            path => Assert.Equal("C:\\Steam", path),
            path => Assert.Equal("D:\\SteamLibrary", path));
    }

    [Fact]
    public void Parse_IgnoresComments()
    {
        const string content = """
"LibraryFolders"
{
    // Primary library
    "0" "C:\\Steam"
}
""";

        var result = _parser.Parse(content);

        Assert.Single(result);
        Assert.Equal("C:\\Steam", result[0]);
    }

    [Fact]
    public void Parse_SupportsEscapedCharacters()
    {
        const string content = """
"LibraryFolders"
{
    "0" "C:\\Games\\Steam\\\"Quotes\\Folder"
}
""";

        var result = _parser.Parse(content);

        Assert.Single(result);
        Assert.Equal("C:\\Games\\Steam\\\"Quotes\\Folder", result[0]);
    }
}
