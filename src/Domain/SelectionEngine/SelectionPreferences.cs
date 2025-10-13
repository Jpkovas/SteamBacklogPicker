namespace Domain.Selection;

public sealed class SelectionPreferences
{
    public SelectionFilters Filters { get; set; } = new();

    public int? Seed { get; set; }

    public int RecentGameExclusionCount { get; set; }

    public int HistoryLimit { get; set; } = 50;

    public SelectionPreferences Clone()
    {
        return new SelectionPreferences
        {
            Filters = (Filters ?? new SelectionFilters()).Clone(),
            Seed = Seed,
            RecentGameExclusionCount = RecentGameExclusionCount,
            HistoryLimit = HistoryLimit,
        };
    }

    internal void Normalize()
    {
        Filters ??= new SelectionFilters();
        Filters.Normalize();
        if (HistoryLimit < 0)
        {
            HistoryLimit = 0;
        }

        if (RecentGameExclusionCount < 0)
        {
            RecentGameExclusionCount = 0;
        }
    }
}
