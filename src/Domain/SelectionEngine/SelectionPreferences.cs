using System;
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

    public bool RequireVr
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.RequireVr;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.RequireVr = value;
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
            Filters.MoodTags = value ?? new List<string>();
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

    public BacklogStatusFilter AllowedBacklogStatuses
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.AllowedBacklogStatuses;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.AllowedBacklogStatuses = value;
        }
    }

    public double InstallStateWeight
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.InstallStateWeight;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.InstallStateWeight = value;
        }
    }

    public double LastPlayedRecencyWeight
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.LastPlayedRecencyWeight;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.LastPlayedRecencyWeight = value;
        }
    }

    public double DeckCompatibilityWeight
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.DeckCompatibilityWeight;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.DeckCompatibilityWeight = value;
        }
    }

    public TimeSpan? MinimumPlaytime
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.MinimumPlaytime;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.MinimumPlaytime = value;
        }
    }

    public TimeSpan? MaximumTargetSessionLength
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.MaximumTargetSessionLength;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.MaximumTargetSessionLength = value;
        }
    }

    public TimeSpan? MaximumEstimatedCompletionTime
    {
        get
        {
            Filters ??= new SelectionFilters();
            return Filters.MaximumEstimatedCompletionTime;
        }
        set
        {
            Filters ??= new SelectionFilters();
            Filters.MaximumEstimatedCompletionTime = value;
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
