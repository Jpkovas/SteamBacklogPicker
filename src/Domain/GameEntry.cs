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

    public GameUserData UserData { get; init; } = GameUserData.Empty;
}
