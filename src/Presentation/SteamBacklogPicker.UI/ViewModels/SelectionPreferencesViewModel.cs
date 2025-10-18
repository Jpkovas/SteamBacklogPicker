using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Domain;
using Domain.Selection;
using SteamBacklogPicker.UI.Services;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class SelectionPreferencesViewModel : ObservableObject
{
    private readonly ISelectionEngine _selectionEngine;
    private readonly ILocalizationService _localizationService;
    private bool _requireInstalled;
    private bool _excludeDeckUnsupported;
    private bool _includeGames = true;
    private bool _includeSoundtracks;
    private bool _includeSoftware;
    private bool _includeTools;
    private bool _includeVideos;
    private bool _includeOther;
    private bool _includeStatusUnspecified = true;
    private bool _includeStatusWishlist = true;
    private bool _includeStatusBacklog = true;
    private bool _includeStatusPlaying = true;
    private bool _includeStatusCompleted = true;
    private bool _includeStatusAbandoned = true;
    private string _maxPlaytimeHours = string.Empty;
    private string _maxSessionHours = string.Empty;
    private string _maxCompletionHours = string.Empty;
    private bool _isHydrating;
    private readonly ObservableCollection<string> _collectionOptions = new();
    private string _noCollectionOption = string.Empty;
    private string _selectedCollection = string.Empty;
    private int _recentGameExclusionCount;

    private static readonly BacklogStatus[] AllStatuses = Enum.GetValues<BacklogStatus>();

    public SelectionPreferencesViewModel(ISelectionEngine selectionEngine, ILocalizationService localizationService)
    {
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _localizationService.LanguageChanged += OnLanguageChanged;

        UpdateNoCollectionOption();

        var preferences = _selectionEngine.GetPreferences();
        Apply(preferences);
    }

    public event EventHandler<SelectionPreferences>? PreferencesChanged;

    public bool RequireInstalled
    {
        get => _requireInstalled;
        set
        {
            if (SetProperty(ref _requireInstalled, value) && !_isHydrating)
            {
                UpdatePreferences(p => p.Filters.RequireInstalled = value);
            }
        }
    }

    public bool ExcludeDeckUnsupported
    {
        get => _excludeDeckUnsupported;
        set
        {
            if (SetProperty(ref _excludeDeckUnsupported, value) && !_isHydrating)
            {
                UpdatePreferences(p => p.Filters.ExcludeDeckUnsupported = value);
            }
        }
    }

    public int RecentGameExclusionCount
    {
        get => _recentGameExclusionCount;
        set
        {
            var normalized = Math.Max(0, value);
            if (SetProperty(ref _recentGameExclusionCount, normalized) && !_isHydrating)
            {
                UpdatePreferences(p => p.RecentGameExclusionCount = normalized);
            }
        }
    }

    public bool IncludeGames
    {
        get => _includeGames;
        set
        {
            if (SetProperty(ref _includeGames, value) && !_isHydrating)
            {
                UpdateCategoryPreferences();
            }
        }
    }

    public bool IncludeSoundtracks
    {
        get => _includeSoundtracks;
        set
        {
            if (SetProperty(ref _includeSoundtracks, value) && !_isHydrating)
            {
                UpdateCategoryPreferences();
            }
        }
    }

    public bool IncludeSoftware
    {
        get => _includeSoftware;
        set
        {
            if (SetProperty(ref _includeSoftware, value) && !_isHydrating)
            {
                UpdateCategoryPreferences();
            }
        }
    }

    public bool IncludeTools
    {
        get => _includeTools;
        set
        {
            if (SetProperty(ref _includeTools, value) && !_isHydrating)
            {
                UpdateCategoryPreferences();
            }
        }
    }

    public bool IncludeVideos
    {
        get => _includeVideos;
        set
        {
            if (SetProperty(ref _includeVideos, value) && !_isHydrating)
            {
                UpdateCategoryPreferences();
            }
        }
    }

    public bool IncludeOther
    {
        get => _includeOther;
        set
        {
            if (SetProperty(ref _includeOther, value) && !_isHydrating)
            {
                UpdateCategoryPreferences();
            }
        }
    }

    public IReadOnlyList<string> CollectionOptions => _collectionOptions;

    public string SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            var noCollection = _noCollectionOption;
            var desired = string.IsNullOrWhiteSpace(value) ? noCollection : value;
            if (string.Equals(desired, noCollection, StringComparison.OrdinalIgnoreCase))
            {
                desired = noCollection;
            }

            if (SetProperty(ref _selectedCollection, desired) && !_isHydrating)
            {
                UpdatePreferences(p => p.Filters.RequiredCollection = desired == noCollection ? null : desired);
            }
        }
    }

    public bool IncludeStatusUnspecified
    {
        get => _includeStatusUnspecified;
        set
        {
            if (SetProperty(ref _includeStatusUnspecified, value) && !_isHydrating)
            {
                UpdateStatusFilters();
            }
        }
    }

    public bool IncludeStatusWishlist
    {
        get => _includeStatusWishlist;
        set
        {
            if (SetProperty(ref _includeStatusWishlist, value) && !_isHydrating)
            {
                UpdateStatusFilters();
            }
        }
    }

    public bool IncludeStatusBacklog
    {
        get => _includeStatusBacklog;
        set
        {
            if (SetProperty(ref _includeStatusBacklog, value) && !_isHydrating)
            {
                UpdateStatusFilters();
            }
        }
    }

    public bool IncludeStatusPlaying
    {
        get => _includeStatusPlaying;
        set
        {
            if (SetProperty(ref _includeStatusPlaying, value) && !_isHydrating)
            {
                UpdateStatusFilters();
            }
        }
    }

    public bool IncludeStatusCompleted
    {
        get => _includeStatusCompleted;
        set
        {
            if (SetProperty(ref _includeStatusCompleted, value) && !_isHydrating)
            {
                UpdateStatusFilters();
            }
        }
    }

    public bool IncludeStatusAbandoned
    {
        get => _includeStatusAbandoned;
        set
        {
            if (SetProperty(ref _includeStatusAbandoned, value) && !_isHydrating)
            {
                UpdateStatusFilters();
            }
        }
    }

    public string MaxPlaytimeHours
    {
        get => _maxPlaytimeHours;
        set
        {
            if (SetProperty(ref _maxPlaytimeHours, NormalizeInput(value)) && !_isHydrating)
            {
                UpdateTimeFilters();
            }
        }
    }

    public string MaxSessionHours
    {
        get => _maxSessionHours;
        set
        {
            if (SetProperty(ref _maxSessionHours, NormalizeInput(value)) && !_isHydrating)
            {
                UpdateTimeFilters();
            }
        }
    }

    public string MaxCompletionHours
    {
        get => _maxCompletionHours;
        set
        {
            if (SetProperty(ref _maxCompletionHours, NormalizeInput(value)) && !_isHydrating)
            {
                UpdateTimeFilters();
            }
        }
    }

    public void Apply(SelectionPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        _isHydrating = true;
        try
        {
            RequireInstalled = preferences.Filters.RequireInstalled;
            ExcludeDeckUnsupported = preferences.Filters.ExcludeDeckUnsupported;

            var categories = preferences.Filters.IncludedCategories ?? new List<ProductCategory>();
            IncludeGames = categories.Contains(ProductCategory.Game);
            IncludeSoundtracks = categories.Contains(ProductCategory.Soundtrack);
            IncludeSoftware = categories.Contains(ProductCategory.Software);
            IncludeTools = categories.Contains(ProductCategory.Tool);
            IncludeVideos = categories.Contains(ProductCategory.Video);
            IncludeOther = categories.Contains(ProductCategory.Other);

            RecentGameExclusionCount = preferences.RecentGameExclusionCount;

            var requiredCollection = preferences.Filters.RequiredCollection;
            if (!string.IsNullOrWhiteSpace(requiredCollection) &&
                !_collectionOptions.Any(option => string.Equals(option, requiredCollection, StringComparison.OrdinalIgnoreCase)))
            {
                _collectionOptions.Add(requiredCollection);
            }

            SelectedCollection = string.IsNullOrWhiteSpace(requiredCollection)
                ? _noCollectionOption
                : requiredCollection;

            var statuses = preferences.Filters.IncludedStatuses ?? new List<BacklogStatus>();
            var includeAll = statuses.Count == 0;
            IncludeStatusUnspecified = includeAll || statuses.Contains(BacklogStatus.Unspecified);
            IncludeStatusWishlist = includeAll || statuses.Contains(BacklogStatus.Wishlist);
            IncludeStatusBacklog = includeAll || statuses.Contains(BacklogStatus.Backlog);
            IncludeStatusPlaying = includeAll || statuses.Contains(BacklogStatus.Playing);
            IncludeStatusCompleted = includeAll || statuses.Contains(BacklogStatus.Completed);
            IncludeStatusAbandoned = includeAll || statuses.Contains(BacklogStatus.Abandoned);

            MaxPlaytimeHours = FormatHours(preferences.Filters.MaxPlaytime);
            MaxSessionHours = FormatHours(preferences.Filters.MaxTargetSessionLength);
            MaxCompletionHours = FormatHours(preferences.Filters.MaxEstimatedCompletionTime);
        }
        finally
        {
            _isHydrating = false;
        }
    }

    public void UpdateCollections(IEnumerable<string> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);

        var normalized = collections
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _isHydrating = true;
        try
        {
            _collectionOptions.Clear();
            _collectionOptions.Add(_noCollectionOption);

            foreach (var name in normalized)
            {
                _collectionOptions.Add(name);
            }

            var matchingSelection = _collectionOptions
                .FirstOrDefault(option => string.Equals(option, _selectedCollection, StringComparison.OrdinalIgnoreCase));

            if (matchingSelection is null)
            {
                _selectedCollection = _noCollectionOption;
                OnPropertyChanged(nameof(SelectedCollection));
            }
            else if (!string.Equals(matchingSelection, _selectedCollection, StringComparison.Ordinal))
            {
                _selectedCollection = matchingSelection;
                OnPropertyChanged(nameof(SelectedCollection));
            }
        }
        finally
        {
            _isHydrating = false;
        }
    }

    public void RefreshLocalization()
    {
        UpdateNoCollectionOption();
    }

    private void UpdatePreferences(Action<SelectionPreferences> updater)
    {
        var preferences = _selectionEngine.GetPreferences();
        updater(preferences);
        _selectionEngine.UpdatePreferences(preferences);
        Apply(preferences);
        PreferencesChanged?.Invoke(this, preferences);
    }

    private void UpdateCategoryPreferences()
    {
        UpdatePreferences(p =>
        {
            p.Filters.IncludedCategories = BuildSelectedCategories();
        });
    }

    private void UpdateStatusFilters()
    {
        UpdatePreferences(p =>
        {
            var selected = BuildSelectedStatuses();
            if (selected.Count == AllStatuses.Length)
            {
                p.Filters.IncludedStatuses = new List<BacklogStatus>();
            }
            else
            {
                p.Filters.IncludedStatuses = selected;
            }
        });
    }

    private void UpdateTimeFilters()
    {
        UpdatePreferences(p =>
        {
            p.Filters.MaxPlaytime = ParseHours(_maxPlaytimeHours);
            p.Filters.MaxTargetSessionLength = ParseHours(_maxSessionHours);
            p.Filters.MaxEstimatedCompletionTime = ParseHours(_maxCompletionHours);
        });
    }

    private List<ProductCategory> BuildSelectedCategories()
    {
        var categories = new List<ProductCategory>();
        if (IncludeGames)
        {
            categories.Add(ProductCategory.Game);
        }

        if (IncludeSoundtracks)
        {
            categories.Add(ProductCategory.Soundtrack);
        }

        if (IncludeSoftware)
        {
            categories.Add(ProductCategory.Software);
        }

        if (IncludeTools)
        {
            categories.Add(ProductCategory.Tool);
        }

        if (IncludeVideos)
        {
            categories.Add(ProductCategory.Video);
        }

        if (IncludeOther)
        {
            categories.Add(ProductCategory.Other);
        }

        return categories;
    }

    private List<BacklogStatus> BuildSelectedStatuses()
    {
        var statuses = new List<BacklogStatus>();
        if (IncludeStatusUnspecified)
        {
            statuses.Add(BacklogStatus.Unspecified);
        }

        if (IncludeStatusWishlist)
        {
            statuses.Add(BacklogStatus.Wishlist);
        }

        if (IncludeStatusBacklog)
        {
            statuses.Add(BacklogStatus.Backlog);
        }

        if (IncludeStatusPlaying)
        {
            statuses.Add(BacklogStatus.Playing);
        }

        if (IncludeStatusCompleted)
        {
            statuses.Add(BacklogStatus.Completed);
        }

        if (IncludeStatusAbandoned)
        {
            statuses.Add(BacklogStatus.Abandoned);
        }

        return statuses;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalization();
    }

    private void UpdateNoCollectionOption()
    {
        var previousNoCollection = _noCollectionOption;
        var previousSelection = _selectedCollection;

        _noCollectionOption = _localizationService.GetString("Filters_NoCollection");

        var isNoneSelected = string.IsNullOrWhiteSpace(previousSelection) ||
                             string.Equals(previousSelection, previousNoCollection, StringComparison.OrdinalIgnoreCase);

        if (_collectionOptions.Count == 0)
        {
            _collectionOptions.Add(_noCollectionOption);
        }
        else
        {
            _collectionOptions[0] = _noCollectionOption;
        }

        if (isNoneSelected)
        {
            _selectedCollection = _noCollectionOption;
            OnPropertyChanged(nameof(SelectedCollection));
        }
    }

    private static string NormalizeInput(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatHours(TimeSpan? value)
    {
        return value.HasValue ? value.Value.TotalHours.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
    }

    private static TimeSpan? ParseHours(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var hours) && hours > 0)
        {
            return TimeSpan.FromHours(hours);
        }

        return null;
    }
}
