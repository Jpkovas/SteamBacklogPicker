using Domain;

namespace EpicDiscovery;

public sealed record class EpicEntitlement
{
    public GameIdentifier Id { get; init; } = GameIdentifier.Unknown;

    public string? CatalogItemId { get; init; }
        
    public string? CatalogNamespace { get; init; }
        
    public string? AppName { get; init; }
        
    public string Title { get; init; } = string.Empty;
}
