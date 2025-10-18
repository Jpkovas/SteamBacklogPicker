using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Domain.Selection;

public sealed class SelectionFilters
{
    public bool RequireInstalled { get; set; }

    public DeckCompatibilityFilter AllowedDeckCompatibility { get; set; } = DeckCompatibilityFilter.All;

    [JsonPropertyName("ExcludeDeckUnsupported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyExcludeDeckUnsupported
    {
        get => !AllowedDeckCompatibility.HasFlag(DeckCompatibilityFilter.Unsupported);
        set => AllowedDeckCompatibility = value
            ? AllowedDeckCompatibility & ~DeckCompatibilityFilter.Unsupported
            : AllowedDeckCompatibility | DeckCompatibilityFilter.Unsupported;
    }

    public string? RequiredCollection { get; set; }

    public List<ProductCategory> IncludedCategories { get; set; } = new() { ProductCategory.Game };

    public SelectionFilters Clone()
    {
        return new SelectionFilters
        {
            RequireInstalled = RequireInstalled,
            AllowedDeckCompatibility = AllowedDeckCompatibility,
            RequiredCollection = RequiredCollection,
            IncludedCategories = IncludedCategories is null ? new List<ProductCategory>() : new List<ProductCategory>(IncludedCategories),
        };
    }

    internal void Normalize()
    {
        AllowedDeckCompatibility &= DeckCompatibilityFilter.All;
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
    }
}
