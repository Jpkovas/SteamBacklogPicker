using System;

namespace Domain;

public enum OwnershipType
{
    Unknown = 0,
    Owned = 1,
    FamilyShared = 2,
}

public enum InstallState
{
    Unknown = 0,
    Installed = 1,
    Available = 2,
    Shared = 3,
}

public enum ProductCategory
{
    Unknown = 0,
    Game = 1,
    Soundtrack = 2,
    Software = 3,
    Tool = 4,
    Video = 5,
    DLC = 6,
    Other = 7,
}

public enum BacklogStatus
{
    Uncategorized = 0,
    Wishlist = 1,
    Backlog = 2,
    Playing = 3,
    Completed = 4,
    Shelved = 5,
    Abandoned = 6,
}

[Flags]
public enum BacklogStatusFilter
{
    None = 0,
    Uncategorized = 1 << 0,
    Wishlist = 1 << 1,
    Backlog = 1 << 2,
    Playing = 1 << 3,
    Completed = 1 << 4,
    Shelved = 1 << 5,
    Abandoned = 1 << 6,
    All = Uncategorized | Wishlist | Backlog | Playing | Completed | Shelved | Abandoned,
}

public sealed record class GameUserData
{
    public BacklogStatus Status { get; init; } = BacklogStatus.Uncategorized;

    public string Notes { get; init; } = string.Empty;

    public TimeSpan? Playtime { get; init; }

    public TimeSpan? TargetSessionLength { get; init; }

    public TimeSpan? EstimatedCompletionTime { get; init; }

    public DateTimeOffset? EstimatedCompletionTimeUpdatedAt { get; init; }

    public GameUserData Normalize()
    {
        var normalizedNotes = string.IsNullOrWhiteSpace(Notes)
            ? string.Empty
            : Notes.Trim();

        return this with
        {
            Notes = normalizedNotes,
            Playtime = NormalizeDuration(Playtime),
            TargetSessionLength = NormalizeDuration(TargetSessionLength),
            EstimatedCompletionTime = NormalizeDuration(EstimatedCompletionTime),
        };
    }

    private static TimeSpan? NormalizeDuration(TimeSpan? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value;
    }
}

public sealed record class GameEntry
{
    public uint AppId { get; init; }

    public string Title { get; init; } = string.Empty;

    public OwnershipType OwnershipType { get; init; } = OwnershipType.Unknown;

    public InstallState InstallState { get; init; } = InstallState.Unknown;

    public ProductCategory ProductCategory { get; init; } = ProductCategory.Game;

    public long? SizeOnDisk { get; init; }

    public DateTimeOffset? LastPlayed { get; init; }

    public IReadOnlyCollection<string> Tags { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<int> StoreCategoryIds { get; init; } = Array.Empty<int>();

    public SteamDeckCompatibility DeckCompatibility { get; init; } = SteamDeckCompatibility.Unknown;

    public GameUserData UserData { get; init; } = new();
}
