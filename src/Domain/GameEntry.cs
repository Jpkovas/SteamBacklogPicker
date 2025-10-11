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

public sealed record class GameEntry
{
    public uint AppId { get; init; }

    public string Title { get; init; } = string.Empty;

    public OwnershipType OwnershipType { get; init; } = OwnershipType.Unknown;

    public InstallState InstallState { get; init; } = InstallState.Unknown;

    public long? SizeOnDisk { get; init; }

    public DateTimeOffset? LastPlayed { get; init; }
}
