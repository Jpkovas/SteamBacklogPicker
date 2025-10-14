using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Domain;
using Domain.Selection;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class SelectionPreferencesViewModel : ObservableObject
{
    private const string NoCollectionOption = "Nenhuma coleção";

    private readonly ISelectionEngine _selectionEngine;
    private bool _requireInstalled;
    private bool _excludeDeckUnsupported;
    private bool _includeGames = true;
    private bool _includeSoundtracks;
    private bool _includeSoftware;
    private bool _includeTools;
    private bool _includeVideos;
    private bool _includeOther;
    private bool _isHydrating;
    private readonly ObservableCollection<string> _collectionOptions = new() { NoCollectionOption };
    private string _selectedCollection = NoCollectionOption;

    public SelectionPreferencesViewModel(ISelectionEngine selectionEngine)
    {
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
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
            var desired = string.IsNullOrWhiteSpace(value) ? NoCollectionOption : value;
            if (SetProperty(ref _selectedCollection, desired) && !_isHydrating)
            {
                UpdatePreferences(p => p.Filters.RequiredCollection = desired == NoCollectionOption ? null : desired);
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

            var requiredCollection = preferences.Filters.RequiredCollection;
            if (!string.IsNullOrWhiteSpace(requiredCollection) &&
                !_collectionOptions.Any(option => string.Equals(option, requiredCollection, StringComparison.OrdinalIgnoreCase)))
            {
                _collectionOptions.Add(requiredCollection);
            }

             SelectedCollection = string.IsNullOrWhiteSpace(requiredCollection)
                 ? NoCollectionOption
                 : requiredCollection;
        }
        finally
        {
            _isHydrating = false;
        }
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
            _collectionOptions.Add(NoCollectionOption);

            foreach (var name in normalized)
            {
                _collectionOptions.Add(name);
            }

            var matchingSelection = _collectionOptions
                .FirstOrDefault(option => string.Equals(option, _selectedCollection, StringComparison.OrdinalIgnoreCase));

            if (matchingSelection is null)
            {
                _selectedCollection = NoCollectionOption;
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
}
