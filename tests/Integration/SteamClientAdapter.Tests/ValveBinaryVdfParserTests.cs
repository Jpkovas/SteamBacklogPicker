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

    [Theory]
    [InlineData(39, false)]
    [InlineData(40, false)]
    [InlineData(41, false)]
    [InlineData(42, true)]
    public void ParseAppInfo_ShouldReadModernVersionsWithBoundedReader(int version, bool compressed)
    {
        using var stream = BuildModernAppInfo(version, compressed);
        var entries = new ValveBinaryVdfParser().ParseAppInfo(stream);
        var entry = Assert.Single(entries);
        Assert.Equal(42u, entry.Key);
        Assert.Equal("Modern game", entry.Value.FindPath("common", "name")!.Value);
        Assert.Equal(version >= 41 ? BitConverter.ToInt64(stream.ToArray(), 8) : stream.Length, stream.Position);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(8L)]
    [InlineData(long.MaxValue)]
    public void ParseAppInfo_ShouldRejectInvalidStringTableOffsets(long offset)
    {
        using var stream = BuildModernAppInfo(41, false);
        stream.Position = 8;
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write(offset);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => new ValveBinaryVdfParser().ParseAppInfo(stream));
    }

    [Fact]
    public void ParseAppInfo_ShouldRejectUnboundedStringTableCount()
    {
        using var stream = BuildModernAppInfo(41, false);
        var offset = BitConverter.ToInt64(stream.ToArray(), 8);
        stream.Position = offset;
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write(uint.MaxValue);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => new ValveBinaryVdfParser().ParseAppInfo(stream));
    }

    [Fact]
    public void ParseAppInfo_ShouldSkipCorruptEntryAndKeepFollowingValidEntry()
    {
        using var stream = BuildModernAppInfo(41, false, corruptFirst: true);
        var entries = new ValveBinaryVdfParser().ParseAppInfo(stream);
        Assert.Equal(42u, Assert.Single(entries).Key);
    }

    [Fact]
    public void ParseAppInfo_ShouldRejectExcessivePayloadNestingAndRetainFollowingEntry()
    {
        using var nested = new MemoryStream();
        using (var writer = new BinaryWriter(nested, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            for (var i = 0; i < 140; i++) { writer.Write((byte)0); writer.Write(0u); }
            for (var i = 0; i < 141; i++) writer.Write((byte)8);
        }
        using var stream = BuildModernAppInfo(41, false, corruptPayload: nested.ToArray());
        Assert.Equal(42u, Assert.Single(new ValveBinaryVdfParser().ParseAppInfo(stream)).Key);
    }

    [Fact]
    public void ParseAppInfo_ShouldNotAcceptIncompleteModernEntryAtAnyTruncation()
    {
        using var complete = BuildModernAppInfo(40, false);
        var bytes = complete.ToArray();
        var entryEnd = bytes.Length - sizeof(uint); // terminal AppID=0
        for (var length = 0; length < entryEnd; length++)
        {
            using var truncated = new MemoryStream(bytes, 0, length, writable: false);
            Assert.Empty(new ValveBinaryVdfParser().ParseAppInfo(truncated));
        }
    }

    [Fact]
    public void ParseAppInfo_ShouldRejectOversizedDocumentsWithoutReadingThem()
    {
        using var stream = new OversizedStream();
        Assert.Throws<InvalidDataException>(() => new ValveBinaryVdfParser().ParseAppInfo(stream));
    }

    private static MemoryStream BuildModernAppInfo(int version, bool compressed, bool corruptFirst = false, byte[]? corruptPayload = null)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)0);
            WriteKey(writer, "common", 0, version);
            writer.Write((byte)1);
            WriteKey(writer, "name", 1, version);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("Modern game\0"));
            writer.Write((byte)8);
            writer.Write((byte)8);
        }
        var data = payload.ToArray();
        if (compressed)
        {
            using var compressor = new ZstdSharp.Compressor();
            var compressedData = compressor.Wrap(data).ToArray();
            using var wrapped = new MemoryStream();
            using var writer = new BinaryWriter(wrapped);
            var checksum = System.IO.Hashing.Crc32.HashToUInt32(data);
            writer.Write(0x615A5356u);
            writer.Write(checksum);
            writer.Write(compressedData);
            writer.Write(checksum);
            writer.Write(data.Length);
            writer.Write(new byte[4]);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("zsv"));
            data = wrapped.ToArray();
        }
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x07564400u | (uint)version);
            writer.Write(1u);
            if (version >= 41) writer.Write(0L);
            if (corruptFirst || corruptPayload is not null)
            {
                // Int32 node with only one of its four value bytes, followed by a valid entry.
                WriteEntry(writer, 41, corruptPayload ?? new byte[] { 2, 1, 0, 0, 0, 7 }, version);
            }
            WriteEntry(writer, 42, data, version);
            writer.Write(0u);
            if (version >= 41)
            {
                var tableOffset = stream.Position;
                writer.Write(2u);
                writer.Write(System.Text.Encoding.UTF8.GetBytes("common\0name\0"));
                stream.Position = 8;
                writer.Write(tableOffset);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteKey(BinaryWriter writer, string text, uint index, int version)
    {
        if (version >= 41) writer.Write(index);
        else writer.Write(System.Text.Encoding.UTF8.GetBytes(text + "\0"));
    }

    private static void WriteEntry(BinaryWriter writer, uint appId, byte[] payload, int version)
    {
        var metadataLength = version >= 40 ? 60 : 40;
        writer.Write(appId);
        writer.Write((uint)(metadataLength + payload.Length));
        writer.Write(new byte[metadataLength]);
        writer.Write(payload);
    }

    private sealed class OversizedStream : MemoryStream
    {
        public override long Length => ValveBinaryVdfParser.MaxDocumentBytes + 1;
    }
}
