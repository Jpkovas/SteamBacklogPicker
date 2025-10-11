namespace Domain.Selection;

public sealed class SelectionHistoryEntry
{
    public uint AppId { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset SelectedAt { get; set; }
}
