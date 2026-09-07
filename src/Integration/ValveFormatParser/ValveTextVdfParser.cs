using System.Text;

namespace ValveFormatParser;

public sealed class ValveTextVdfParser
{
    public const int MaxDocumentCharacters = 64 * 1024 * 1024;
    public const int MaxTokenCharacters = 1024 * 1024;
    public const int MaxDepth = 128;
    public const int MaxNodes = 1_000_000;

    public ValveKeyValueNode Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaxDocumentCharacters) throw new InvalidDataException("VDF document is too large.");
        return new Parser(new StringReader(content)).Parse();
    }

    public ValveKeyValueNode Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return new Parser(reader).Parse();
    }

    private enum TokenKind { Text, Open, Close }
    private readonly record struct Token(TokenKind Kind, string Value);

    private sealed class Parser(TextReader reader)
    {
        private int _characters;
        private int _nodes;

        public ValveKeyValueNode Parse()
        {
            var root = ValveKeyValueNode.CreateObject("root");
            ReadObject(root, 0);
            return root;
        }

        private int Read()
        {
            var value = reader.Read();
            if (value != -1 && ++_characters > MaxDocumentCharacters)
                throw new InvalidDataException("VDF document is too large.");
            return value;
        }

        private void ReadObject(ValveKeyValueNode parent, int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("VDF nesting limit exceeded.");
            while (NextToken() is { } key)
            {
                if (key.Kind == TokenKind.Close)
                {
                    if (depth == 0) throw new InvalidDataException("Unexpected object end.");
                    return;
                }
                if (key.Kind != TokenKind.Text) throw new InvalidDataException("Expected a VDF key.");
                var value = NextToken() ?? throw new InvalidDataException("Missing VDF value.");
                if (++_nodes > MaxNodes) throw new InvalidDataException("VDF node limit exceeded.");
                if (value.Kind == TokenKind.Open)
                {
                    var child = ValveKeyValueNode.CreateObject(key.Value);
                    ReadObject(child, depth + 1);
                    parent.AddChild(child);
                }
                else if (value.Kind == TokenKind.Text)
                {
                    parent.AddChild(ValveKeyValueNode.CreateValue(key.Value, value.Value));
                }
                else throw new InvalidDataException("Missing VDF value before object end.");
            }
            if (depth != 0) throw new InvalidDataException("Unterminated VDF object.");
        }

        private Token? NextToken()
        {
            int first;
            while (true)
            {
                first = Read();
                if (first == -1) return null;
                if (char.IsWhiteSpace((char)first)) continue;
                if (first == '/' && reader.Peek() == '/')
                {
                    while ((first = Read()) != -1 && first != '\n') { }
                    continue;
                }
                break;
            }
            if (first == '{') return new Token(TokenKind.Open, "{");
            if (first == '}') return new Token(TokenKind.Close, "}");
            var quoted = first == '"';
            var text = new StringBuilder();
            if (!quoted) text.Append((char)first);
            while (true)
            {
                if (text.Length > MaxTokenCharacters) throw new InvalidDataException("VDF token is too large.");
                var next = reader.Peek();
                if (!quoted && (next == -1 || char.IsWhiteSpace((char)next) || next is '{' or '}'))
                    return new Token(TokenKind.Text, text.ToString());
                next = Read();
                if (next == -1) throw new InvalidDataException("Unterminated quoted VDF string.");
                if (quoted && next == '"') return new Token(TokenKind.Text, text.ToString());
                if (quoted && next == '\\')
                {
                    var escape = Read();
                    if (escape == -1) throw new InvalidDataException("Unterminated VDF escape.");
                    text.Append(escape switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => (char)escape });
                }
                else text.Append((char)next);
            }
        }
    }
}
