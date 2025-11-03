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

    public List<Storefront> IncludedStorefronts { get; set; } = new();

    public SelectionFilters Clone()
    {
        return new SelectionFilters
        {
            RequireInstalled = RequireInstalled,
            ExcludeDeckUnsupported = ExcludeDeckUnsupported,
            RequiredCollection = RequiredCollection,
            IncludedCategories = IncludedCategories is null ? new List<ProductCategory>() : new List<ProductCategory>(IncludedCategories),
            IncludedStorefronts = IncludedStorefronts is null ? new List<Storefront>() : new List<Storefront>(IncludedStorefronts),
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

        IncludedStorefronts ??= new List<Storefront>();
        if (IncludedStorefronts.Count > 0)
        {
            IncludedStorefronts = IncludedStorefronts
                .Where(store => store != Storefront.Unknown)
                .Distinct()
                .ToList();
        }
    }
}
