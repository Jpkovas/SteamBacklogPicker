using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ValveFormatParser;

public sealed class ValveBinaryVdfParser
{
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

        var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var result = new Dictionary<uint, ValveKeyValueNode>();

        while (stream.Position < stream.Length)
        {
            var appId = reader.ReadUInt32();
            var size = reader.ReadUInt32();

            if (appId == 0 && size == 0)
            {
                break;
            }

            if (size > int.MaxValue)
            {
                // The payload is too large to buffer in memory. Stop parsing to avoid
                // attempting to allocate an excessively large array.
                break;
            }

            if (stream.CanSeek)
            {
                var remaining = stream.Length - stream.Position;
                if (size > remaining)
                {
                    // The declared size would read past the end of the stream. Treat the
                    // remainder of the file as truncated and stop parsing.
                    break;
                }
            }

            var payload = reader.ReadBytes((int)size);
            if (payload.Length != (int)size)
            {
                // The payload could not be read in full which indicates truncated data.
                break;
            }
            using var payloadStream = new MemoryStream(payload, writable: false);
            using var payloadReader = new BinaryReader(payloadStream, Encoding.UTF8);
            try
            {
                SkipAppInfoMetadata(payloadReader, appId, size);
                var node = ParseNode(payloadReader);
                result[appId] = node;
            }
            catch (EndOfStreamException)
            {
                // Individual entries in appinfo.vdf can become truncated. Skip the
                // affected entry and continue parsing any remaining ones.
            }
            catch (InvalidDataException)
            {
                // Ignore malformed entries so that valid application data can still be
                // returned to callers.
            }
        }

        return result;
    }

    private static void SkipAppInfoMetadata(BinaryReader reader, uint appId, uint size)
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

    private static ValveKeyValueNode ParseNode(BinaryReader reader)
    {
        var root = ValveKeyValueNode.CreateObject("appinfo");
        ReadChildren(reader, root);
        return root;
    }

    private static void ReadChildren(BinaryReader reader, ValveKeyValueNode parent)
    {
        while (true)
        {
            var type = (ValveBinaryType)reader.ReadByte();
            if (type == ValveBinaryType.End || type == ValveBinaryType.AlternateEnd)
            {
                return;
            }

            var key = ReadNullTerminatedString(reader);
            switch (type)
            {
                case ValveBinaryType.Child:
                    {
                        var child = ValveKeyValueNode.CreateObject(key);
                        parent.AddChild(child);
                        ReadChildren(reader, child);
                        break;
                    }
                case ValveBinaryType.String:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, ReadNullTerminatedString(reader)));
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
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, ReadWideString(reader)));
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

    private static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte value;
        while ((value = reader.ReadByte()) != 0)
        {
            bytes.Add(value);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string ReadWideString(BinaryReader reader)
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
}
