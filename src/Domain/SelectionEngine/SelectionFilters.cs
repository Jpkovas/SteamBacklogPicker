using System;
using System.Collections.Generic;
using System.Linq;

namespace Domain.Selection;

public sealed class SelectionFilters
{
    public bool RequireInstalled { get; set; }

    public bool ExcludeDeckUnsupported { get; set; }

    public bool RequireSinglePlayer { get; set; }

    public bool RequireMultiplayer { get; set; }

    public bool RequireVirtualReality { get; set; }

    public string? RequiredCollection { get; set; }

    public List<ProductCategory> IncludedCategories { get; set; } = new() { ProductCategory.Game };

    public List<string> MoodTags { get; set; } = new();

    public SelectionFilters Clone()
    {
        return new SelectionFilters
        {
            RequireInstalled = RequireInstalled,
            ExcludeDeckUnsupported = ExcludeDeckUnsupported,
            RequireSinglePlayer = RequireSinglePlayer,
            RequireMultiplayer = RequireMultiplayer,
            RequireVirtualReality = RequireVirtualReality,
            RequiredCollection = RequiredCollection,
            IncludedCategories = IncludedCategories is null ? new List<ProductCategory>() : new List<ProductCategory>(IncludedCategories),
            MoodTags = MoodTags is null ? new List<string>() : new List<string>(MoodTags),
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

        MoodTags ??= new List<string>();
        if (MoodTags.Count > 0)
        {
            MoodTags = MoodTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
