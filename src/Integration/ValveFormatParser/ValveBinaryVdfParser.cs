using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Text;
using ValveKeyValue;
using ZstdSharp;

namespace ValveFormatParser;

public sealed class ValveBinaryVdfParser
{
    private const uint ExpectedMagic = 0x075644;
    private const uint ValveZstdMagic = 0x615A5356; // VSZa
    private const int ValveZstdHeaderLength = 8;
    private const int ValveZstdFooterLength = 15;

    private enum ValveBinaryType : byte
    {
        Child = 0,
        String = 1,
        Int32 = 2,
        Float32 = 3,
        Pointer = 4,
        WideString = 5,
        Color = 6,
        UInt64 = 7,
        End = 8,
        Int64 = 0x0A,
        AlternateEnd = 0x0B,
        UInt32 = 0x0C,
        BinaryBlob = 0x0D,
        Boolean = 0x14,
    }

    public IDictionary<uint, ValveKeyValueNode> ParseAppInfo(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanSeek)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            return ParseAppInfo(buffer);
        }

        var origin = stream.Position;
        var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        IDictionary<uint, ValveKeyValueNode>? result = null;

        if (stream.Length - stream.Position >= sizeof(uint))
        {
            var raw = reader.ReadUInt32();
            stream.Position = origin;

            var magicWithoutVersion = raw >> 8;
            if (magicWithoutVersion == ExpectedMagic)
            {
                result = ParseModernAppInfo(stream);
            }
        }

        if (result is null)
        {
            stream.Position = origin;
            result = ParseLegacyAppInfo(stream);
        }

        return result;
    }

    private static IDictionary<uint, ValveKeyValueNode> ParseModernAppInfo(Stream stream)
    {
        var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var result = new Dictionary<uint, ValveKeyValueNode>();

        if (!TryReadHeader(reader, out var version, out var options))
        {
            return result;
        }

        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary);

        while (stream.Position < stream.Length)
        {
            if (!TryReadEntry(reader, version, options, serializer, out var appId, out var node))
            {
                break;
            }

            if (node is not null)
            {
                result[appId] = node;
            }
        }

        return result;
    }

    private static bool TryReadHeader(BinaryReader reader, out byte version, out KVSerializerOptions options)
    {
        options = new KVSerializerOptions();
        version = 0;

        if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(uint) + sizeof(uint))
        {
            return false;
        }

        var magic = reader.ReadUInt32();
        version = (byte)(magic & 0xFF);
        magic >>= 8;

        if (magic != ExpectedMagic)
        {
            throw new InvalidDataException($"Unknown appinfo header magic: 0x{magic:X}");
        }

        if (version is < 39 or > 42)
        {
            throw new InvalidDataException($"Unsupported appinfo header version: {version}");
        }

        // Universe (unused)
        reader.ReadUInt32();

        if (version >= 41)
        {
            var stringTableOffset = reader.ReadInt64();
            var returnPosition = reader.BaseStream.Position;

            reader.BaseStream.Position = stringTableOffset;
            var stringCount = reader.ReadUInt32();
            var strings = new string[stringCount];
            for (var i = 0; i < stringCount; i++)
            {
                strings[i] = ReadNullTerminatedUtf8String(reader.BaseStream);
            }

            options.StringTable = new StringTable(strings);
            reader.BaseStream.Position = returnPosition;
        }

        return true;
    }

    private static bool TryReadEntry(
        BinaryReader reader,
        byte version,
        KVSerializerOptions options,
        KVSerializer serializer,
        out uint appId,
        out ValveKeyValueNode? node)
    {
        node = null;
        appId = 0;

        if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(uint) * 2)
        {
            return false;
        }

        appId = reader.ReadUInt32();
        var size = reader.ReadUInt32();

        if (appId == 0 && size == 0)
        {
            return false;
        }

        var endPosition = reader.BaseStream.Position + size;
        if (reader.BaseStream.Length < endPosition)
        {
            reader.BaseStream.Position = reader.BaseStream.Length;
            return false;
        }

        try
        {
            ReadEntryMetadata(reader, version);

            var payloadLength = (int)(endPosition - reader.BaseStream.Position);
            if (payloadLength < 0)
            {
                throw new InvalidDataException($"App {appId} payload length is negative.");
            }

            if (payloadLength == 0)
            {
                return true;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(payloadLength);
            try
            {
                var bytesRead = reader.Read(buffer, 0, payloadLength);
                if (bytesRead != payloadLength)
                {
                    throw new EndOfStreamException($"Unexpected end of stream while reading app {appId} payload.");
                }

                node = DeserializePayload(buffer, payloadLength, serializer, options);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or IOException or KeyValueException or ZstdException)
        {
            // Skip malformed entries to preserve compatibility with truncated appinfo caches.
        }
        finally
        {
            reader.BaseStream.Position = endPosition;
        }

        return true;
    }

    private static ValveKeyValueNode? DeserializePayload(
        byte[] buffer,
        int length,
        KVSerializer serializer,
        KVSerializerOptions options)
    {
        if (length == 0)
        {
            return null;
        }

        if (IsValveZstdPayload(buffer, length))
        {
            return DeserializeValveZstdPayload(buffer, length, serializer, options);
        }

        using var payloadStream = new MemoryStream(buffer, 0, length, writable: false, publiclyVisible: true);
        var kvObject = serializer.Deserialize(payloadStream, options);
        return ConvertToNode(kvObject);
    }

    private static bool IsValveZstdPayload(byte[] buffer, int length)
    {
        if (length < ValveZstdHeaderLength)
        {
            return false;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(buffer, 0, sizeof(uint)));
        return magic == ValveZstdMagic;
    }

    private static ValveKeyValueNode DeserializeValveZstdPayload(
        byte[] buffer,
        int length,
        KVSerializer serializer,
        KVSerializerOptions options)
    {
        if (length < ValveZstdHeaderLength + ValveZstdFooterLength)
        {
            throw new InvalidDataException("Valve Zstd payload is too small to contain header and footer.");
        }

        var payload = new ReadOnlySpan<byte>(buffer, 0, length);
        var footerOffset = length - ValveZstdFooterLength;
        var footer = payload.Slice(footerOffset, ValveZstdFooterLength);

        var expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, sizeof(uint)));
        var footerChecksum = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(0, sizeof(uint)));
        if (expectedChecksum != footerChecksum)
        {
            throw new InvalidDataException("Valve Zstd payload checksum mismatch.");
        }

        if (footer[ValveZstdFooterLength - 3] != (byte)'z' ||
            footer[ValveZstdFooterLength - 2] != (byte)'s' ||
            footer[ValveZstdFooterLength - 1] != (byte)'v')
        {
            throw new InvalidDataException("Valve Zstd footer marker not found.");
        }

        var decompressedSize = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(4, sizeof(int)));
        if (decompressedSize < 0)
        {
            throw new InvalidDataException("Valve Zstd payload reported a negative decompressed size.");
        }

        var decompressedBuffer = ArrayPool<byte>.Shared.Rent(decompressedSize);
        try
        {
            var written = DecompressValveZstd(payload, decompressedBuffer.AsSpan(0, decompressedSize));
            using var decompressedStream = new MemoryStream(decompressedBuffer, 0, written, writable: false, publiclyVisible: true);
            var kvObject = serializer.Deserialize(decompressedStream, options);
            return ConvertToNode(kvObject);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(decompressedBuffer);
        }
    }

    private static int DecompressValveZstd(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        if (payload.Length < ValveZstdHeaderLength + ValveZstdFooterLength)
        {
            throw new InvalidDataException("Valve Zstd payload is too small.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, sizeof(uint)));
        if (magic != ValveZstdMagic)
        {
            throw new InvalidDataException("Valve Zstd header magic mismatch.");
        }

        var footer = payload.Slice(payload.Length - ValveZstdFooterLength, ValveZstdFooterLength);
        var expectedSize = BinaryPrimitives.ReadInt32LittleEndian(footer.Slice(4, sizeof(int)));
        if (expectedSize < 0)
        {
            throw new InvalidDataException("Valve Zstd payload reports a negative size.");
        }

        if (destination.Length < expectedSize)
        {
            throw new ArgumentException("Destination buffer is smaller than the expected decompressed size.", nameof(destination));
        }

        var compressed = payload.Slice(ValveZstdHeaderLength, payload.Length - (ValveZstdHeaderLength + ValveZstdFooterLength));

        using var decompressor = new Decompressor();
        if (!decompressor.TryUnwrap(compressed, destination, out var written) || written != expectedSize)
        {
            throw new InvalidDataException($"Failed to decompress Valve Zstd payload (expected {expectedSize} bytes, got {written}).");
        }

        var expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(0, sizeof(uint)));
        var actualChecksum = Crc32.HashToUInt32(destination.Slice(0, written));
        if (expectedChecksum != actualChecksum)
        {
            throw new InvalidDataException("Valve Zstd payload checksum validation failed.");
        }

        return written;
    }

    private static void ReadEntryMetadata(BinaryReader reader, byte version)
    {
        reader.ReadUInt32(); // info state
        reader.ReadUInt32(); // last updated
        reader.ReadUInt64(); // access token

        const int checksumLength = 20;
        var checksum = reader.ReadBytes(checksumLength);
        if (checksum.Length != checksumLength)
        {
            throw new EndOfStreamException("Incomplete appinfo entry checksum.");
        }

        reader.ReadUInt32(); // change number

        if (version >= 40)
        {
            var binaryHash = reader.ReadBytes(checksumLength);
            if (binaryHash.Length != checksumLength)
            {
                throw new EndOfStreamException("Incomplete appinfo entry binary hash.");
            }
        }
    }

    private static IDictionary<uint, ValveKeyValueNode> ParseLegacyAppInfo(Stream stream)
    {
        var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var result = new Dictionary<uint, ValveKeyValueNode>();

        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < sizeof(uint) * 2)
            {
                break;
            }

            var appId = reader.ReadUInt32();
            var size = reader.ReadUInt32();

            if (appId == 0 && size == 0)
            {
                break;
            }

            if (size > int.MaxValue)
            {
                break;
            }

            var payloadStart = stream.Position;

            if (stream.CanSeek)
            {
                var remaining = stream.Length - stream.Position;
                if (size > remaining)
                {
                    stream.Seek(stream.Length, SeekOrigin.Begin);
                    break;
                }
            }

            var payload = reader.ReadBytes((int)size);
            if (payload.Length != (int)size)
            {
                if (stream.CanSeek)
                {
                    var nextEntryPosition = payloadStart + size;
                    if (nextEntryPosition > stream.Length)
                    {
                        nextEntryPosition = stream.Length;
                    }

                    stream.Seek(nextEntryPosition, SeekOrigin.Begin);
                }

                continue;
            }

            using var payloadStream = new MemoryStream(payload, writable: false);
            using var payloadReader = new BinaryReader(payloadStream, Encoding.UTF8);
            try
            {
                SkipLegacyMetadata(payloadReader, appId, size);
                var node = ParseLegacyNode(payloadReader);
                result[appId] = node;
            }
            catch (EndOfStreamException)
            {
                if (stream.CanSeek)
                {
                    stream.Seek(payloadStart + size, SeekOrigin.Begin);
                }
            }
            catch (InvalidDataException)
            {
                if (stream.CanSeek)
                {
                    stream.Seek(payloadStart + size, SeekOrigin.Begin);
                }
            }
        }

        return result;
    }

    private static void SkipLegacyMetadata(BinaryReader reader, uint appId, uint size)
    {
        const int checksumLength = 20;
        const int metadataLength = sizeof(uint) + sizeof(uint) + sizeof(ulong) + checksumLength + sizeof(uint);

        if (size < metadataLength)
        {
            throw new InvalidDataException($"App {appId} metadata is smaller than the expected {metadataLength} bytes.");
        }

        reader.ReadUInt32(); // state
        reader.ReadUInt32(); // last updated
        reader.ReadUInt64(); // access token

        var checksumBytes = reader.ReadBytes(checksumLength);
        if (checksumBytes.Length != checksumLength)
        {
            throw new EndOfStreamException($"Unexpected end of stream while reading app {appId} metadata checksum.");
        }

        reader.ReadUInt32(); // change number
    }

    private static ValveKeyValueNode ParseLegacyNode(BinaryReader reader)
    {
        var root = ValveKeyValueNode.CreateObject("appinfo");
        ReadLegacyChildren(reader, root);
        return root;
    }

    private static void ReadLegacyChildren(BinaryReader reader, ValveKeyValueNode parent)
    {
        while (true)
        {
            var type = (ValveBinaryType)reader.ReadByte();
            if (type == ValveBinaryType.End || type == ValveBinaryType.AlternateEnd)
            {
                return;
            }

            var key = ReadLegacyNullTerminatedString(reader);
            switch (type)
            {
                case ValveBinaryType.Child:
                {
                    var child = ValveKeyValueNode.CreateObject(key);
                    parent.AddChild(child);
                    ReadLegacyChildren(reader, child);
                    break;
                }
                case ValveBinaryType.String:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, ReadLegacyNullTerminatedString(reader)));
                    break;
                case ValveBinaryType.Int32:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadInt32().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.Float32:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadSingle().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.Int64:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadInt64().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.UInt64:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadUInt64().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.Pointer:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadUInt32().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.UInt32:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadUInt32().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.WideString:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, ReadLegacyWideString(reader)));
                    break;
                case ValveBinaryType.Color:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadUInt32().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.BinaryBlob:
                {
                    var length = reader.ReadInt32();
                    if (length < 0)
                    {
                        throw new InvalidDataException($"Binary data for key '{key}' has a negative length ({length}).");
                    }

                    var bytes = reader.ReadBytes(length);
                    if (bytes.Length != length)
                    {
                        throw new EndOfStreamException($"Unexpected end of stream while reading binary data for key '{key}'.");
                    }

                    parent.AddChild(ValveKeyValueNode.CreateValue(key, Convert.ToBase64String(bytes)));
                    break;
                }
                case ValveBinaryType.Boolean:
                {
                    var value = reader.ReadByte();
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, value != 0 ? "1" : "0"));
                    break;
                }
                default:
                    throw new InvalidDataException($"Unsupported Valve binary type: {type}.");
            }
        }
    }

    private static ValveKeyValueNode ConvertToNode(KVObject kvObject)
    {
        if (kvObject.Value.ValueType is KVValueType.Collection or KVValueType.Array)
        {
            var node = ValveKeyValueNode.CreateObject(kvObject.Name);
            foreach (var child in kvObject.Children)
            {
                node.AddChild(ConvertToNode(child));
            }

            return node;
        }

        var value = kvObject.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return ValveKeyValueNode.CreateValue(kvObject.Name, value);
    }

    private static string ReadLegacyNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte value;
        while ((value = reader.ReadByte()) != 0)
        {
            bytes.Add(value);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string ReadLegacyWideString(BinaryReader reader)
    {
        var sb = new StringBuilder();
        while (true)
        {
            var character = reader.ReadUInt16();
            if (character == 0)
            {
                break;
            }

            sb.Append((char)character);
        }

        return sb.ToString();
    }

    private static string ReadNullTerminatedUtf8String(Stream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(32);

        try
        {
            var length = 0;

            while (true)
            {
                var next = stream.ReadByte();
                if (next <= 0)
                {
                    break;
                }

                if (length >= buffer.Length)
                {
                    var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = newBuffer;
                }

                buffer[length++] = (byte)next;
            }

            return Encoding.UTF8.GetString(buffer, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
