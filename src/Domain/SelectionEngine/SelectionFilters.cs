namespace Domain.Selection;

public sealed class SelectionFilters
{
    public bool RequireInstalled { get; set; }

    public bool IncludeFamilyShared { get; set; } = true;

    public List<string> RequiredTags { get; set; } = new();

    public long? MinimumSizeOnDisk { get; set; }

    public long? MaximumSizeOnDisk { get; set; }

    public SelectionFilters Clone()
    {
        return new SelectionFilters
        {
            RequireInstalled = RequireInstalled,
            IncludeFamilyShared = IncludeFamilyShared,
            RequiredTags = RequiredTags is null ? new List<string>() : new List<string>(RequiredTags),
            MinimumSizeOnDisk = MinimumSizeOnDisk,
            MaximumSizeOnDisk = MaximumSizeOnDisk,
        };
    }

    internal void Normalize()
    {
        RequiredTags ??= new List<string>();
    }
}
