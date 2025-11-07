using System.Collections.Generic;
using Domain;

namespace EpicDiscovery;

public sealed record class EpicCatalogItem
{
    public GameIdentifier Id { get; init; } = GameIdentifier.Unknown;

    public string? CatalogItemId { get; init; }

    public string? CatalogNamespace { get; init; }

    public string? AppName { get; init; }

    public string Title { get; init; } = string.Empty;

    public IReadOnlyCollection<string> Tags { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<EpicKeyImage> KeyImages { get; init; } = Array.Empty<EpicKeyImage>();

    public long? SizeOnDisk { get; init; }

    public DateTimeOffset? LastModified { get; init; }
}
