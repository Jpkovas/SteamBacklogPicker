using System.Linq;
using System.Text;

namespace SteamDiscovery;

public sealed class SteamLibraryFoldersParser : ISteamLibraryFoldersParser
{
    public IReadOnlyList<string> Parse(string content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var tokenizer = new VdfTokenizer(content);
        var parser = new VdfParser(tokenizer);
        var root = parser.Parse();

        if (!root.TryGetValue("LibraryFolders", out var libraryFolders))
        {
            return Array.Empty<string>();
        }

        var collector = new List<string>();
        CollectPaths(libraryFolders, collector);
        return collector
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CollectPaths(
        VdfElement element,
        ICollection<string> collector,
        string? currentKey = null,
        string? parentKey = null)
    {
        if (element.Value is not null)
        {
            if (currentKey is not null && IsLibraryKey(currentKey, parentKey))
            {
                collector.Add(element.Value);
            }
            return;
        }

        if (element.Children is null)
        {
            return;
        }

        if (currentKey is not null
            && IsLibraryKey(currentKey, parentKey)
            && TryExtractPath(element.Children, out var nestedPath))
        {
            collector.Add(nestedPath);
        }

        foreach (var pair in element.Children)
        {
            CollectPaths(pair.Value, collector, pair.Key, currentKey);
        }
    }

    private static bool IsLibraryKey(string key, string? parentKey)
    {
        if (key.Equals("path", StringComparison.OrdinalIgnoreCase)
            || key.Equals("contentpath", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!int.TryParse(key, out _))
        {
            return false;
        }

        return parentKey is null
               || parentKey.Equals("LibraryFolders", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractPath(IReadOnlyDictionary<string, VdfElement> children, out string? path)
    {
        if (children.TryGetValue("path", out var pathElement) && pathElement.Value is not null)
        {
            path = pathElement.Value;
            return true;
        }

        if (children.TryGetValue("contentpath", out var contentPathElement) && contentPathElement.Value is not null)
        {
            path = contentPathElement.Value;
            return true;
        }

        path = null;
        return false;
    }

    private readonly record struct VdfElement(string? Value, IReadOnlyDictionary<string, VdfElement>? Children)
    {
        public string? Value { get; } = Value;
        public IReadOnlyDictionary<string, VdfElement>? Children { get; } = Children;
    }

    private sealed class VdfParser
    {
        private readonly VdfTokenizer _tokenizer;

        public VdfParser(VdfTokenizer tokenizer)
        {
            _tokenizer = tokenizer;
        }

        public Dictionary<string, VdfElement> Parse()
        {
            var result = new Dictionary<string, VdfElement>(StringComparer.OrdinalIgnoreCase);
            while (_tokenizer.TryPeek(out var token))
            {
                if (token.Type == VdfTokenType.CloseBrace)
                {
                    _tokenizer.Read();
                    break;
                }

                var key = ExpectString();
                var value = ReadValue();
                result[key] = value;
            }

            return result;
        }

        private VdfElement ReadValue()
        {
            if (!_tokenizer.TryPeek(out var token))
            {
                throw new FormatException("Unexpected end of VDF content.");
            }

            if (token.Type == VdfTokenType.OpenBrace)
            {
                _tokenizer.Read();
                var obj = Parse();
                return new VdfElement(null, obj);
            }

            if (token.Type == VdfTokenType.String)
            {
                _tokenizer.Read();
                return new VdfElement(token.Value, null);
            }

            throw new FormatException($"Unexpected token '{token.Type}'.");
        }

        private string ExpectString()
        {
            if (!_tokenizer.TryPeek(out var token) || token.Type != VdfTokenType.String)
            {
                throw new FormatException("Expected string token.");
            }

            _tokenizer.Read();
            return token.Value ?? string.Empty;
        }
    }

    private sealed class VdfTokenizer
    {
        private readonly string _content;
        private int _position;
        private VdfToken? _buffered;

        public VdfTokenizer(string content)
        {
            _content = content;
        }

        public bool TryPeek(out VdfToken token)
        {
            if (_buffered is null)
            {
                _buffered = ReadNextToken();
            }

            if (_buffered is null)
            {
                token = default;
                return false;
            }

            token = _buffered.Value;
            return true;
        }

        public VdfToken Read()
        {
            if (_buffered is null)
            {
                _buffered = ReadNextToken();
            }

            if (_buffered is null)
            {
                throw new InvalidOperationException("Unexpected end of VDF content.");
            }

            var token = _buffered.Value;
            _buffered = null;
            return token;
        }

        private VdfToken? ReadNextToken()
        {
            SkipWhitespace();

            if (_position >= _content.Length)
            {
                return null;
            }

            var current = _content[_position];

            return current switch
            {
                '{' => CreateSimpleToken(VdfTokenType.OpenBrace),
                '}' => CreateSimpleToken(VdfTokenType.CloseBrace),
                '"' => new VdfToken(VdfTokenType.String, ReadString()),
                '/' when PeekNext('/') => SkipComment(),
                _ => throw new FormatException($"Unexpected character '{current}'.")
            };
        }

        private VdfToken? SkipComment()
        {
            _position += 2; // Skip "//"
            while (_position < _content.Length && _content[_position] != '\n')
            {
                _position++;
            }

            return ReadNextToken();
        }

        private void SkipWhitespace()
        {
            while (_position < _content.Length)
            {
                var c = _content[_position];
                if (!char.IsWhiteSpace(c))
                {
                    break;
                }

                _position++;
            }
        }

        private bool PeekNext(char expected)
        {
            if (_position + 1 >= _content.Length)
            {
                return false;
            }

            return _content[_position + 1] == expected;
        }

        private string ReadString()
        {
            var builder = new StringBuilder();
            _position++; // Skip opening quote

            while (_position < _content.Length)
            {
                var c = _content[_position++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c == '\\')
                {
                    if (_position >= _content.Length)
                    {
                        throw new FormatException("Unterminated escape sequence in string value.");
                    }

                    var escape = _content[_position++];
                    builder.Append(escape switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        _ => escape
                    });
                    continue;
                }

                builder.Append(c);
            }

            throw new FormatException("Unterminated string literal in VDF content.");
        }

        private VdfToken CreateSimpleToken(VdfTokenType type)
        {
            _position++;
            return new VdfToken(type, null);
        }
    }

    private readonly record struct VdfToken(VdfTokenType Type, string? Value);

    private enum VdfTokenType
    {
        String,
        OpenBrace,
        CloseBrace
    }
}
