using SteamDiscovery;
using Xunit;

namespace SteamDiscovery.Tests;

public sealed class SteamLibraryFoldersParserTests
{
    private readonly SteamLibraryFoldersParser _parser = new();

    [Fact]
    public void Parse_ReturnsEmpty_WhenLibraryFoldersSectionIsMissing()
    {
        const string content = "\"OtherSection\" { \"0\" \"C:\\\\Steam\" }";

        var result = _parser.Parse(content);

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_ReturnsLegacyPaths()
    {
        const string content = "\"LibraryFolders\"\n{" +
                                "\n    \"0\" \"C:\\\\Program Files (x86)\\\\Steam\"" +
                                "\n    \"1\" \"D:\\\\SteamLibrary\"" +
                                "\n}";

        var result = _parser.Parse(content);

        Assert.Collection(result,
            path => Assert.Equal("C:\\Program Files (x86)\\Steam", path),
            path => Assert.Equal("D:\\SteamLibrary", path));
    }

    [Fact]
    public void Parse_ReturnsPathsFromObjectNotation()
    {
        const string content = "\"LibraryFolders\"\n{" +
                                "\n    \"0\"\n    {\n        \"path\" \"C:\\\\Steam\"\n    }" +
                                "\n    \"1\"\n    {\n        \"path\" \"D:\\\\Games\\\\Steam\"\n        \"label\" \"Secondary\"\n    }" +
                                "\n}";

        var result = _parser.Parse(content);

        Assert.Collection(result,
            path => Assert.Equal("C:\\Steam", path),
            path => Assert.Equal("D:\\Games\\Steam", path));
    }

    [Fact]
    public void Parse_UsesContentPathFallback()
    {
        const string content = "\"LibraryFolders\"\n{" +
                                "\n    \"2\"\n    {\n        \"contentpath\" \"E:\\\\SteamContent\"\n    }" +
                                "\n}";

        var result = _parser.Parse(content);

        Assert.Single(result);
        Assert.Equal("E:\\SteamContent", result[0]);
    }

    [Fact]
    public void Parse_IgnoresMetadataEntries()
    {
        const string content = "\"LibraryFolders\"\n{" +
                                "\n    \"TimeNextStatsReport\" \"12345\"" +
                                "\n    \"ContentStatsID\" \"67890\"" +
                                "\n    \"1\" \"E:\\\\SteamLibrary\"" +
                                "\n}";

        var result = _parser.Parse(content);

        Assert.Single(result);
        Assert.Equal("E:\\SteamLibrary", result[0]);
    }

    [Fact]
    public void Parse_TrimsAndDeduplicatesPaths()
    {
        const string content = "\"LibraryFolders\"\n{" +
                                "\n    \"0\" \" C:\\\\Steam \\"" +
                                "\n    \"1\"\n    {\n        \"path\" \"C:\\\\Steam\"\n    }" +
                                "\n    \"2\" \"D:\\\\SteamLibrary\"" +
                                "\n    \"path\" \"D:\\\\SteamLibrary\"" +
                                "\n}";

        var result = _parser.Parse(content);

        Assert.Collection(result,
            path => Assert.Equal("C:\\Steam", path),
            path => Assert.Equal("D:\\SteamLibrary", path));
    }

    [Fact]
    public void Parse_IgnoresComments()
    {
        const string content = "\"LibraryFolders\"\n{" +
                                "\n    // Primary library" +
                                "\n    \"0\" \"C:\\\\Steam\"" +
                                "\n}";

        var result = _parser.Parse(content);

        Assert.Single(result);
        Assert.Equal("C:\\Steam", result[0]);
    }

    [Fact]
    public void Parse_SupportsEscapedCharacters()
    {
        const string content = "\"LibraryFolders\"\n{" +
                                "\n    \"0\" \"C:\\\\Games\\\\Steam\\\\\\\"Quotes\\\\Folder\"" +
                                "\n}";

        var result = _parser.Parse(content);

        Assert.Single(result);
        Assert.Equal("C:\\Games\\Steam\\\"Quotes\\Folder", result[0]);
    }
}
