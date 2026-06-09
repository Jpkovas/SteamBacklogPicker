using System.Collections.Generic;
using System.Linq;
using Domain;

namespace Domain.Selection;

public sealed class SelectionFilters
{
    public bool RequireInstalled { get; set; }

    public bool ExcludeDeckUnsupported { get; set; }

    public string? RequiredCollection { get; set; }

    public List<ProductCategory> IncludedCategories { get; set; } = new() { ProductCategory.Game };

    public bool FilterByStorefront { get; set; }

    public List<Storefront>? IncludedStorefronts { get; set; }

    public SelectionFilters Clone()
    {
        return new SelectionFilters
        {
            RequireInstalled = RequireInstalled,
            ExcludeDeckUnsupported = ExcludeDeckUnsupported,
            RequiredCollection = RequiredCollection,
            IncludedCategories = IncludedCategories is null ? new List<ProductCategory>() : new List<ProductCategory>(IncludedCategories),
            FilterByStorefront = FilterByStorefront,
            IncludedStorefronts = IncludedStorefronts is null ? null : new List<Storefront>(IncludedStorefronts),
        };
    }

    internal void Normalize()
    {
        RequiredCollection = string.IsNullOrWhiteSpace(RequiredCollection)
            ? null
            : RequiredCollection.Trim();
        if (IncludedCategories is null)
        {
            IncludedCategories = new List<ProductCategory> { ProductCategory.Game };
        }
        else
        {
            IncludedCategories = IncludedCategories
                .Distinct()
                .ToList();
        }

        if (IncludedStorefronts is not null)
        {
            IncludedStorefronts = IncludedStorefronts
                .Where(store => store != Storefront.Unknown)
                .Distinct()
                .ToList();
        }
    }
}
