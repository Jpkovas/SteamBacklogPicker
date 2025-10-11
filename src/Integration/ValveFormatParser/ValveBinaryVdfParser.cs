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

            var payload = reader.ReadBytes((int)size);
            using var payloadStream = new MemoryStream(payload, writable: false);
            using var payloadReader = new BinaryReader(payloadStream, Encoding.UTF8);
            var node = ParseNode(payloadReader);
            result[appId] = node;
        }

        return result;
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
            if (type == ValveBinaryType.End)
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
                case ValveBinaryType.UInt64:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadUInt64().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.Pointer:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadUInt32().ToString(CultureInfo.InvariantCulture)));
                    break;
                case ValveBinaryType.WideString:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, ReadWideString(reader)));
                    break;
                case ValveBinaryType.Color:
                    parent.AddChild(ValveKeyValueNode.CreateValue(key, reader.ReadUInt32().ToString(CultureInfo.InvariantCulture)));
                    break;
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
