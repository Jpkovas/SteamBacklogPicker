using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ValveFormatParser;

public sealed class ValveKeyValueNode
{
    private readonly Dictionary<string, ValveKeyValueNode> _children;

    private ValveKeyValueNode(string name, string? value, IReadOnlyDictionary<string, ValveKeyValueNode>? children)
    {
        Name = name;
        Value = value;
        _children = children is null
            ? new Dictionary<string, ValveKeyValueNode>(StringComparer.Ordinal)
            : new Dictionary<string, ValveKeyValueNode>(children, StringComparer.Ordinal);
    }

    public string Name { get; }

    public string? Value { get; }

    public bool IsObject => _children.Count > 0;

    public IReadOnlyDictionary<string, ValveKeyValueNode> Children => new ReadOnlyDictionary<string, ValveKeyValueNode>(_children);

    public static ValveKeyValueNode CreateObject(string name)
        => new(name, null, null);

    public static ValveKeyValueNode CreateValue(string name, string value)
        => new(name, value, null);

    public void AddChild(ValveKeyValueNode child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        _children[child.Name] = child;
    }

    public bool TryGetChild(string name, out ValveKeyValueNode child)
        => _children.TryGetValue(name, out child!);

    public ValveKeyValueNode? FindPath(params string[] segments)
    {
        ValveKeyValueNode? current = this;
        foreach (var segment in segments)
        {
            if (current is null || !current.TryGetChild(segment, out current))
            {
                return null;
            }
        }

        return current;
    }

    public bool TryGetBoolean(out bool value)
    {
        if (Value is null)
        {
            value = false;
            return false;
        }

        if (int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            value = numeric != 0;
            return true;
        }

        if (bool.TryParse(Value, out value))
        {
            return true;
        }

        value = false;
        return false;
    }

    public override string ToString()
        => Value ?? "{" + string.Join(", ", _children.Keys) + "}";
}
