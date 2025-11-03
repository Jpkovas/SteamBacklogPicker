using Domain;

namespace Domain.Selection;

public sealed class SelectionHistoryEntry
{
    public GameIdentifier Id { get; set; } = GameIdentifier.Unknown;

    public uint AppId
    {
        get => Id.SteamAppId ?? 0;
        set
        {
            if (value == 0)
            {
                if (Id != GameIdentifier.Unknown && Id.Storefront != Storefront.Steam)
                {
                    return;
                }

                Id = GameIdentifier.Unknown;
                return;
            }

            Id = GameIdentifier.ForSteam(value);
        }
    }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset SelectedAt { get; set; }
}
