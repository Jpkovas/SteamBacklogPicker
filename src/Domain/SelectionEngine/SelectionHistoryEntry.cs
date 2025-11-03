using Domain;

namespace Domain.Selection;

public sealed class SelectionHistoryEntry
{
    public GameIdentifier Id { get; set; } = GameIdentifier.Unknown;

    public uint AppId
    {
        get => Id.SteamAppId ?? 0;
        set => Id = value == 0 ? GameIdentifier.Unknown : GameIdentifier.ForSteam(value);
    }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset SelectedAt { get; set; }
}
