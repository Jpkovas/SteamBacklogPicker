using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ValveFormatParser;

public sealed class ValveTextVdfParser
{
    public ValveKeyValueNode Parse(string content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        using var reader = new StringReader(content);
        return Parse(reader);
    }

    public ValveKeyValueNode Parse(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return Parse(reader);
    }

    private static ValveKeyValueNode Parse(TextReader reader)
    {
        var root = ValveKeyValueNode.CreateObject("root");
        ParseObjectInto(reader, root);
        return root;
    }

    private static void ParseObjectInto(TextReader reader, ValveKeyValueNode current)
    {
        while (true)
        {
            var token = ReadToken(reader);
            if (token is null)
            {
                return;
            }

            if (token == "}")
            {
                return;
            }

            if (token == "{")
            {
                throw new InvalidDataException("Unexpected object start.");
            }

            var value = ReadToken(reader);
            if (value is null)
            {
                throw new InvalidDataException("Unexpected end of VDF while parsing value.");
            }

            if (value == "{")
            {
                var child = ValveKeyValueNode.CreateObject(token);
                current.AddChild(child);
                ParseObjectInto(reader, child);
            }
            else if (value == "}")
            {
                throw new InvalidDataException("Unexpected object end.");
            }
            else
            {
                current.AddChild(ValveKeyValueNode.CreateValue(token, value));
            }
        }
    }

    private static string? ReadToken(TextReader reader)
    {
        var sb = new StringBuilder();
        int next;
        bool insideQuotes = false;

        while (true)
        {
            next = reader.Read();
            if (next == -1)
            {
                if (insideQuotes)
                {
                    throw new InvalidDataException("Unterminated quoted string.");
                }

                return sb.Length > 0 ? sb.ToString() : null;
            }

            var ch = (char)next;
            if (insideQuotes)
            {
                if (ch == '\\')
                {
                    var escape = reader.Read();
                    if (escape == -1)
                    {
                        throw new InvalidDataException("Invalid escape sequence in quoted string.");
                    }

                    sb.Append((char)escape);
                    continue;
                }

                if (ch == '"')
                {
                    return sb.ToString();
                }

                sb.Append(ch);
                continue;
            }

            if (ch == '"')
            {
                insideQuotes = true;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0)
                {
                    return sb.ToString();
                }

                continue;
            }

            if (ch is '{' or '}')
            {
                if (sb.Length > 0)
                {
                    throw new InvalidDataException("Unexpected token before structural character.");
                }

                return ch.ToString(CultureInfo.InvariantCulture);
            }

            sb.Append(ch);
        }
    }
}
