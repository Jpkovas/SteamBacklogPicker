using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private bool _includeDeckUnknown = true;
    private bool _includeDeckVerified = true;
    private bool _includeDeckPlayable = true;
    private bool _includeDeckUnsupported = true;
    private bool _includeGames = true;
    private bool _includeSoundtracks;
    private bool _includeSoftware;
    private bool _includeTools;
    private bool _includeVideos;
    private bool _includeOther;
    private bool _isHydrating;
    private readonly ObservableCollection<string> _collectionOptions = new();
    private string _noCollectionOption = string.Empty;
    private string _selectedCollection = string.Empty;
    private int _recentGameExclusionCount;

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

    public bool IncludeDeckUnknown
    {
        get => _includeDeckUnknown;
        set
        {
            if (SetProperty(ref _includeDeckUnknown, value) && !_isHydrating)
            {
                UpdateDeckCompatibilityPreferences();
            }
        }
    }

    public bool IncludeDeckVerified
    {
        get => _includeDeckVerified;
        set
        {
            if (SetProperty(ref _includeDeckVerified, value) && !_isHydrating)
            {
                UpdateDeckCompatibilityPreferences();
            }
        }
    }

    public bool IncludeDeckPlayable
    {
        get => _includeDeckPlayable;
        set
        {
            if (SetProperty(ref _includeDeckPlayable, value) && !_isHydrating)
            {
                UpdateDeckCompatibilityPreferences();
            }
        }
    }

    public bool IncludeDeckUnsupported
    {
        get => _includeDeckUnsupported;
        set
        {
            if (SetProperty(ref _includeDeckUnsupported, value) && !_isHydrating)
            {
                UpdateDeckCompatibilityPreferences();
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

    public void Apply(SelectionPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        _isHydrating = true;
        try
        {
            RequireInstalled = preferences.Filters.RequireInstalled;
            var allowedCompatibility = preferences.Filters.AllowedDeckCompatibility;
            IncludeDeckUnknown = allowedCompatibility.HasFlag(DeckCompatibilityFilter.Unknown);
            IncludeDeckVerified = allowedCompatibility.HasFlag(DeckCompatibilityFilter.Verified);
            IncludeDeckPlayable = allowedCompatibility.HasFlag(DeckCompatibilityFilter.Playable);
            IncludeDeckUnsupported = allowedCompatibility.HasFlag(DeckCompatibilityFilter.Unsupported);

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

    private void UpdateDeckCompatibilityPreferences()
    {
        UpdatePreferences(p =>
        {
            var allowed = DeckCompatibilityFilter.None;
            if (IncludeDeckUnknown)
            {
                allowed |= DeckCompatibilityFilter.Unknown;
            }

            if (IncludeDeckVerified)
            {
                allowed |= DeckCompatibilityFilter.Verified;
            }

            if (IncludeDeckPlayable)
            {
                allowed |= DeckCompatibilityFilter.Playable;
            }

            if (IncludeDeckUnsupported)
            {
                allowed |= DeckCompatibilityFilter.Unsupported;
            }

            p.Filters.AllowedDeckCompatibility = allowed;
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
}
