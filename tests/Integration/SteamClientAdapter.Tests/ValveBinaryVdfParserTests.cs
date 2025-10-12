using System;
using System.IO;
using SteamTestUtilities.ValveFormat;
using ValveFormatParser;
using Xunit;

namespace SteamClientAdapter.Tests;

public sealed class ValveBinaryVdfParserTests
{
    [Fact]
    public void ParseAppInfo_SkipsEntriesWhenPayloadIsTruncated()
    {
        var parser = new ValveBinaryVdfParser();
        var fixturePath = Path.Combine(VdfFixtureLoader.RootDirectory, "steam", "appcache", "appinfo.vdf");
        var bytes = File.ReadAllBytes(fixturePath);

        // Guard against line ending normalization corrupting the binary fixture when
        // the repository is checked out on Windows runners. The binary header stores
        // the application identifier and payload size as little-endian 32-bit
        // integers. If Git converts the file to CRLF the header bytes change and the
        // parser will observe nonsense values, leading to empty results.
        Assert.Equal(10u, BitConverter.ToUInt32(bytes, 0));
        Assert.Equal(87u, BitConverter.ToUInt32(bytes, sizeof(uint)));

        var firstPayloadSize = (int)BitConverter.ToUInt32(bytes, 4);
        var firstEntryLength = Math.Min(bytes.Length, sizeof(uint) + sizeof(uint) + firstPayloadSize);

        using var stream = new MemoryStream();
        stream.Write(bytes, 0, firstEntryLength);

        var truncatedHeader = new byte[sizeof(uint) * 2];
        BitConverter.GetBytes(30u).CopyTo(truncatedHeader, 0);
        BitConverter.GetBytes(50u).CopyTo(truncatedHeader, sizeof(uint));
        stream.Write(truncatedHeader, 0, truncatedHeader.Length);
        stream.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        stream.Write(new byte[sizeof(uint) * 2]);
        stream.Position = 0;

        var entries = parser.ParseAppInfo(stream);

        Assert.Contains(10u, entries.Keys);
        Assert.DoesNotContain(30u, entries.Keys);
    }
}
