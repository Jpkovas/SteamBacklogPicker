using System.IO;
using System.Linq;
using System.Text;
using ValveFormatParser;
using Xunit;

namespace SteamClientAdapter.Tests;

public sealed class ValveTextVdfParserTests
{
    [Fact]
    public void Parse_ShouldPreserveQuotedBracesAndEscapedPaths()
    {
        var root = new ValveTextVdfParser().Parse("root { title \"{\" path \"C:\\\\Steam\" }");
        Assert.Equal("{", root.FindPath("root", "title")!.Value);
        Assert.Equal(@"C:\Steam", root.FindPath("root", "path")!.Value);
    }

    [Theory]
    [InlineData("root {")]
    [InlineData("root { key \"value\"")]
    [InlineData("root { key \"unterminated")]
    [InlineData("root { key }")]
    [InlineData("}")]
    public void Parse_ShouldRejectTruncatedOrUnbalancedDocuments(string text)
    {
        Assert.Throws<InvalidDataException>(() => new ValveTextVdfParser().Parse(text));
    }

    [Fact]
    public void Parse_ShouldSkipLongRunsOfCommentsWithoutRecursion()
    {
        var text = string.Concat(Enumerable.Repeat("// comment\n", 20_000)) + "root { key value }";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        Assert.Equal("value", new ValveTextVdfParser().Parse(stream).FindPath("root", "key")!.Value);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Parse_ShouldBoundNestingBeforeStackExhaustion()
    {
        var text = string.Concat(Enumerable.Repeat("node { ", ValveTextVdfParser.MaxDepth + 1)) +
                   new string('}', ValveTextVdfParser.MaxDepth + 1);
        Assert.Throws<InvalidDataException>(() => new ValveTextVdfParser().Parse(text));
    }

    [Fact]
    public void Parse_ShouldBoundSingleTokens()
    {
        var text = "root { key \"" + new string('x', ValveTextVdfParser.MaxTokenCharacters + 1) + "\" }";
        Assert.Throws<InvalidDataException>(() => new ValveTextVdfParser().Parse(text));
    }
}
