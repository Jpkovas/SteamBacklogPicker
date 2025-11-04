namespace EpicDiscovery;

public sealed record class EpicKeyImage
{
    public string Type { get; init; } = string.Empty;

    public string? Uri { get; init; }

    public string? Path { get; init; }
}
