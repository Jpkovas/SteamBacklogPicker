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

    [Fact]
    public void ParseAppInfo_ReadsBinaryTypeNineAsBlob()
    {
        var parser = new ValveBinaryVdfParser();
        using var stream = BuildStreamWithTypeNineEntry();

        var entries = parser.ParseAppInfo(stream);

        Assert.True(entries.TryGetValue(123u, out var node));
        var binaryNode = node.FindPath("appinfo", "binaryData");
        Assert.NotNull(binaryNode);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), binaryNode!.Value);
    }

    private static MemoryStream BuildStreamWithTypeNineEntry()
    {
        const byte Child = 0x00;
        const byte End = 0x08;
        const byte BinaryTypeNine = 0x09;

        using var payload = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            payloadWriter.Write(0u); // state
            payloadWriter.Write(0u); // last updated
            payloadWriter.Write(0ul); // access token
            payloadWriter.Write(new byte[20]); // checksum
            payloadWriter.Write(1u); // change number

            payloadWriter.Write(Child);
            WriteNullTerminatedString(payloadWriter, "appinfo");

            payloadWriter.Write(BinaryTypeNine);
            WriteNullTerminatedString(payloadWriter, "binaryData");
            payloadWriter.Write(3);
            payloadWriter.Write(new byte[] { 1, 2, 3 });

            payloadWriter.Write(End); // end of child object
            payloadWriter.Write(End); // end of root object
        }

        var payloadBytes = payload.ToArray();
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(123u); // app id
            writer.Write((uint)payloadBytes.Length);
            writer.Write(payloadBytes);
            writer.Write(0u);
            writer.Write(0u);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteNullTerminatedString(BinaryWriter writer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Write(bytes);
        writer.Write((byte)0);
    }
}
