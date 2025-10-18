using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Domain.Selection;

public sealed class SelectionFilters
{
    public bool RequireInstalled { get; set; }

    public bool RequireSinglePlayer { get; set; }

    public bool RequireMultiplayer { get; set; }

    public bool RequireVr { get; set; }

    public DeckCompatibilityFilter AllowedDeckCompatibility { get; set; } = DeckCompatibilityFilter.All;

    public BacklogStatusFilter AllowedBacklogStatuses { get; set; } = BacklogStatusFilter.All;

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

    public List<string> MoodTags { get; set; } = new();

    public double InstallStateWeight { get; set; } = 1d;

    public double LastPlayedRecencyWeight { get; set; } = 1d;

    public double DeckCompatibilityWeight { get; set; } = 1d;

    public TimeSpan? MinimumPlaytime { get; set; }

    public TimeSpan? MaximumTargetSessionLength { get; set; }

    public TimeSpan? MaximumEstimatedCompletionTime { get; set; }

    public SelectionFilters Clone()
    {
        return new SelectionFilters
        {
            RequireInstalled = RequireInstalled,
            RequireSinglePlayer = RequireSinglePlayer,
            RequireMultiplayer = RequireMultiplayer,
            RequireVr = RequireVr,
            AllowedDeckCompatibility = AllowedDeckCompatibility,
            AllowedBacklogStatuses = AllowedBacklogStatuses,
            RequiredCollection = RequiredCollection,
            IncludedCategories = IncludedCategories is null ? new List<ProductCategory>() : new List<ProductCategory>(IncludedCategories),
            MoodTags = MoodTags is null ? new List<string>() : new List<string>(MoodTags),
            InstallStateWeight = InstallStateWeight,
            LastPlayedRecencyWeight = LastPlayedRecencyWeight,
            DeckCompatibilityWeight = DeckCompatibilityWeight,
            MinimumPlaytime = MinimumPlaytime,
            MaximumTargetSessionLength = MaximumTargetSessionLength,
            MaximumEstimatedCompletionTime = MaximumEstimatedCompletionTime,
        };
    }

    internal void Normalize()
    {
        AllowedDeckCompatibility &= DeckCompatibilityFilter.All;
        if (AllowedBacklogStatuses == BacklogStatusFilter.None)
        {
            AllowedBacklogStatuses = BacklogStatusFilter.All;
        }
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

        InstallStateWeight = ClampWeight(InstallStateWeight);
        LastPlayedRecencyWeight = ClampWeight(LastPlayedRecencyWeight);
        DeckCompatibilityWeight = ClampWeight(DeckCompatibilityWeight);

        MoodTags = NormalizeMoodTags(MoodTags);
        MinimumPlaytime = NormalizeDuration(MinimumPlaytime);
        MaximumTargetSessionLength = NormalizeDuration(MaximumTargetSessionLength);
        MaximumEstimatedCompletionTime = NormalizeDuration(MaximumEstimatedCompletionTime);
    }

    private static double ClampWeight(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1d;
        }

        return Math.Clamp(value, 0d, 2d);
    }

    private static List<string> NormalizeMoodTags(List<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return new List<string>();
        }

        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static TimeSpan? NormalizeDuration(TimeSpan? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value;
    }
}
