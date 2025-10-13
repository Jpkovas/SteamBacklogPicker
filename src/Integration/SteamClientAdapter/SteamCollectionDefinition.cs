using System;
using System.Collections.Generic;

namespace SteamClientAdapter;

public sealed class SteamCollectionDefinition
{
    public SteamCollectionDefinition(
        string id,
        string name,
        IReadOnlyCollection<uint> explicitAppIds,
        CollectionFilterSpec? filterSpec)
    {
        Id = id;
        Name = name;
        ExplicitAppIds = explicitAppIds ?? Array.Empty<uint>();
        FilterSpec = filterSpec;
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyCollection<uint> ExplicitAppIds { get; }

    public CollectionFilterSpec? FilterSpec { get; }
}

public sealed class CollectionFilterSpec
{
    public CollectionFilterSpec(IReadOnlyList<CollectionFilterGroup> groups)
    {
        Groups = groups ?? Array.Empty<CollectionFilterGroup>();
    }

    public IReadOnlyList<CollectionFilterGroup> Groups { get; }
}

public sealed class CollectionFilterGroup
{
    public CollectionFilterGroup(IReadOnlyList<int> options, bool acceptUnion)
    {
        Options = options ?? Array.Empty<int>();
        AcceptUnion = acceptUnion;
    }

    public IReadOnlyList<int> Options { get; }

    public bool AcceptUnion { get; }
}
