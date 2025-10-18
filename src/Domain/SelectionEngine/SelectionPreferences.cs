namespace Domain.Selection;

public sealed class SelectionPreferences
{
    public SelectionFilters Filters { get; set; } = new();

    public bool ExcludeDeckUnsupported
    {
        get
        {
            Filters ??= new SelectionFilters();
            return !Filters.AllowedDeckCompatibility.HasFlag(DeckCompatibilityFilter.Unsupported);
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.AllowedDeckCompatibility = value
                ? Filters.AllowedDeckCompatibility & ~DeckCompatibilityFilter.Unsupported
                : Filters.AllowedDeckCompatibility | DeckCompatibilityFilter.Unsupported;
        }
    }

    public DeckCompatibilityFilter AllowedDeckCompatibility
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.AllowedDeckCompatibility;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.AllowedDeckCompatibility = value;
        }
    }

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
