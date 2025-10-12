using System;
using System.Collections.Generic;

namespace SteamClientAdapter;

public sealed record class SteamAppDefinition
{
    private IReadOnlyList<string> _collections = Array.Empty<string>();

    public SteamAppDefinition(uint AppId, string? Name, bool IsInstalled, IReadOnlyList<string>? Collections)
    {
        this.AppId = AppId;
        this.Name = Name;
        this.IsInstalled = IsInstalled;
        this.Collections = Collections ?? Array.Empty<string>();
    }

    public uint AppId { get; init; }

    public string? Name { get; init; }

    public bool IsInstalled { get; init; }

    public IReadOnlyList<string> Collections
    {
        get => _collections;
        init => _collections = value ?? Array.Empty<string>();
    }
}
