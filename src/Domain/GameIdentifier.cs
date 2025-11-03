using System;
using System.Collections.Generic;
using System.Globalization;

namespace Domain;

public sealed record class GameIdentifier : IComparable<GameIdentifier>
{
    private static readonly StringComparer StoreIdComparer = StringComparer.OrdinalIgnoreCase;

    private string storeSpecificId = string.Empty;

    public Storefront Storefront { get; init; } = Storefront.Unknown;

    public string StoreSpecificId
    {
        get => storeSpecificId;
        init => storeSpecificId = NormalizeStoreSpecificId(value);
    }

    public uint? SteamAppId { get; init; }

    public static GameIdentifier Unknown { get; } = new();

    public static IComparer<GameIdentifier> Comparer { get; } = Comparer<GameIdentifier>.Create(
        static (left, right) => left.CompareTo(right));

    public static GameIdentifier ForSteam(uint appId)
    {
        return new GameIdentifier
        {
            Storefront = Storefront.Steam,
            StoreSpecificId = appId.ToString(CultureInfo.InvariantCulture),
            SteamAppId = appId,
        };
    }

    public int CompareTo(GameIdentifier? other)
    {
        if (other is null)
        {
            return 1;
        }

        var storefrontComparison = Storefront.CompareTo(other.Storefront);
        if (storefrontComparison != 0)
        {
            return storefrontComparison;
        }

        var storeIdComparison = string.Compare(StoreSpecificId, other.StoreSpecificId, StringComparison.OrdinalIgnoreCase);
        if (storeIdComparison != 0)
        {
            return storeIdComparison;
        }

        return Nullable.Compare(SteamAppId, other.SteamAppId);
    }

    public override string ToString()
    {
        return $"{Storefront}:{StoreSpecificId}";
    }

    public virtual bool Equals(GameIdentifier? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return Storefront == other.Storefront &&
               StoreIdComparer.Equals(StoreSpecificId, other.StoreSpecificId) &&
               Nullable.Equals(SteamAppId, other.SteamAppId);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Storefront, StoreIdComparer.GetHashCode(StoreSpecificId), SteamAppId);
    }

    private static string NormalizeStoreSpecificId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
