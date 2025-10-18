using System;
using System.Collections.Generic;
using System.Linq;

namespace Domain.Selection;

public sealed class SelectionFilters
{
    public bool RequireInstalled { get; set; }

    public bool ExcludeDeckUnsupported { get; set; }

    public string? RequiredCollection { get; set; }

    public List<ProductCategory> IncludedCategories { get; set; } = new() { ProductCategory.Game };

    public List<BacklogStatus> IncludedStatuses { get; set; } = new();

    public TimeSpan? MaxPlaytime { get; set; }

    public TimeSpan? MaxTargetSessionLength { get; set; }

    public TimeSpan? MaxEstimatedCompletionTime { get; set; }

    public SelectionFilters Clone()
    {
        return new SelectionFilters
        {
            RequireInstalled = RequireInstalled,
            ExcludeDeckUnsupported = ExcludeDeckUnsupported,
            RequiredCollection = RequiredCollection,
            IncludedCategories = IncludedCategories is null ? new List<ProductCategory>() : new List<ProductCategory>(IncludedCategories),
            IncludedStatuses = IncludedStatuses is null ? new List<BacklogStatus>() : new List<BacklogStatus>(IncludedStatuses),
            MaxPlaytime = MaxPlaytime,
            MaxTargetSessionLength = MaxTargetSessionLength,
            MaxEstimatedCompletionTime = MaxEstimatedCompletionTime,
        };
    }

    internal void Normalize()
    {
        RequiredCollection = string.IsNullOrWhiteSpace(RequiredCollection)
            ? null
            : RequiredCollection.Trim();
        IncludedCategories ??= new List<ProductCategory>();
        if (IncludedCategories.Count == 0)
        {
            IncludedCategories.Add(ProductCategory.Game);
        }
        else
        {
            IncludedCategories = IncludedCategories
                .Distinct()
                .ToList();
        }

        IncludedStatuses ??= new List<BacklogStatus>();
        if (IncludedStatuses.Count > 0)
        {
            IncludedStatuses = IncludedStatuses
                .Where(status => Enum.IsDefined(status))
                .Distinct()
                .ToList();
        }

        MaxPlaytime = NormalizeThreshold(MaxPlaytime);
        MaxTargetSessionLength = NormalizeThreshold(MaxTargetSessionLength);
        MaxEstimatedCompletionTime = NormalizeThreshold(MaxEstimatedCompletionTime);
    }

    private static TimeSpan? NormalizeThreshold(TimeSpan? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value <= TimeSpan.Zero ? null : value;
    }
}
