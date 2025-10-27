namespace Domain.Selection;

internal sealed class SelectionSettings
{
    public SelectionPreferences Preferences { get; set; } = new();

    public List<SelectionHistoryEntry> History { get; set; } = new();

    public int RandomPosition { get; set; }
}
