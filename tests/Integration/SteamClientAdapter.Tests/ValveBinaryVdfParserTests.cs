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

        var firstPayloadSize = (int)BitConverter.ToUInt32(bytes, 4);
        var firstEntryLength = sizeof(uint) + sizeof(uint) + firstPayloadSize;
        var safeLength = Math.Min(firstEntryLength, bytes.Length);

        using var stream = new MemoryStream();
        stream.Write(bytes, 0, safeLength);

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
