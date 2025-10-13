using System;
using System.Collections.Generic;
using Domain;

namespace SteamClientAdapter;

public sealed record class SteamAppDefinition
{
    private IReadOnlyList<string> _collections = Array.Empty<string>();
    private IReadOnlyList<int> _storeCategoryIds = Array.Empty<int>();

    public SteamAppDefinition(uint AppId, string? Name, bool IsInstalled, string? Type, IReadOnlyList<string>? Collections)
    {
        this.AppId = AppId;
        this.Name = Name;
        this.IsInstalled = IsInstalled;
        this.Type = Type;
        this.Collections = Collections ?? Array.Empty<string>();
    }

    public uint AppId { get; init; }

    public string? Name { get; init; }

    public bool IsInstalled { get; init; }

    public string? Type { get; init; }

    public IReadOnlyList<string> Collections
    {
        get => _collections;
        init => _collections = value ?? Array.Empty<string>();
    }

    public IReadOnlyList<int> StoreCategoryIds
    {
        get => _storeCategoryIds;
        init => _storeCategoryIds = value ?? Array.Empty<int>();
    }

    public SteamDeckCompatibility DeckCompatibility { get; init; } = SteamDeckCompatibility.Unknown;
}
