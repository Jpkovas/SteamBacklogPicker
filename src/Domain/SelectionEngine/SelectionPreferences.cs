using System.Collections.Generic;

namespace Domain.Selection;

public sealed class SelectionPreferences
{
    public SelectionFilters Filters { get; set; } = new();

    public bool ExcludeDeckUnsupported
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.ExcludeDeckUnsupported;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.ExcludeDeckUnsupported = value;
        }
    }

    public bool RequireSinglePlayer
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.RequireSinglePlayer;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.RequireSinglePlayer = value;
        }
    }

    public bool RequireMultiplayer
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.RequireMultiplayer;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.RequireMultiplayer = value;
        }
    }

    public bool RequireVirtualReality
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.RequireVirtualReality;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.RequireVirtualReality = value;
        }
    }

    public List<string> MoodTags
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.MoodTags;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.MoodTags = value is null ? new List<string>() : new List<string>(value);
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
