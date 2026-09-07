using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Domain;
using Domain.Selection;
using SteamBacklogPicker.UI.Services.Localization;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class SelectionPreferencesViewModel : ObservableObject, IDisposable
{
    private readonly ISelectionEngine _selectionEngine;
    private readonly ILocalizationService _localizationService;
    private bool _requireInstalled;
    private bool _excludeDeckUnsupported;
    private bool _requireMacCompatible;
    private bool _includeGames = true;
    private bool _includeSoundtracks;
    private bool _includeSoftware;
    private bool _includeTools;
    private bool _includeVideos;
    private bool _includeOther;
    private bool _includeSteam = true;
    private bool _isHydrating;
    private readonly SnapshotCollection<string> _collectionOptions = new();
    private string _noCollectionOption = string.Empty;
    private string _selectedCollection = string.Empty;
    private string? _lastSaveError;

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
    public string? LastSaveError { get => _lastSaveError; private set => SetProperty(ref _lastSaveError, value); }

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

    public bool RequireMacCompatible
    {
        get => _requireMacCompatible;
        set
        {
            if (SetProperty(ref _requireMacCompatible, value) && !_isHydrating)
            {
                UpdatePreferences(p => p.Filters.RequireMacCompatible = value);
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
            // Rebuilding ComboBox items can send a temporary null through a TwoWay binding.
            // Hydration restores its captured selection explicitly; this is not a user edit.
            if (_isHydrating) return;

            var noCollection = _noCollectionOption;
            var desired = string.IsNullOrWhiteSpace(value) ? noCollection : value;
            if (string.Equals(desired, noCollection, StringComparison.OrdinalIgnoreCase))
            {
                desired = noCollection;
            }

            if (SetProperty(ref _selectedCollection, desired))
            {
                UpdatePreferences(p => p.Filters.RequiredCollection = desired == noCollection ? null : desired);
            }
        }
    }

    public bool IncludeSteam
    {
        get => _includeSteam;
        set
        {
            if (SetProperty(ref _includeSteam, value) && !_isHydrating)
            {
                UpdateStorefrontPreferences();
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
            RequireMacCompatible = preferences.Filters.RequireMacCompatible;

            var categories = preferences.Filters.IncludedCategories ?? new List<ProductCategory>();
            IncludeGames = categories.Contains(ProductCategory.Game);
            IncludeSoundtracks = categories.Contains(ProductCategory.Soundtrack);
            IncludeSoftware = categories.Contains(ProductCategory.Software);
            IncludeTools = categories.Contains(ProductCategory.Tool);
            IncludeVideos = categories.Contains(ProductCategory.Video);
            IncludeOther = categories.Contains(ProductCategory.Other);

            var storefronts = preferences.Filters.IncludedStorefronts ?? new List<Storefront>();
            if (!preferences.Filters.FilterByStorefront)
            {
                IncludeSteam = true;
            }
            else
            {
                IncludeSteam = storefronts.Contains(Storefront.Steam);
            }

            var requiredCollection = preferences.Filters.RequiredCollection;
            if (!string.IsNullOrWhiteSpace(requiredCollection) &&
                !_collectionOptions.Any(option => string.Equals(option, requiredCollection, StringComparison.OrdinalIgnoreCase)))
            {
                _collectionOptions.Add(requiredCollection);
            }

            var selection = string.IsNullOrWhiteSpace(requiredCollection)
                ? _noCollectionOption
                : _collectionOptions.First(option => string.Equals(option, requiredCollection, StringComparison.OrdinalIgnoreCase));
            SetProperty(ref _selectedCollection, selection, nameof(SelectedCollection));
        }
        finally
        {
            _isHydrating = false;
        }
    }

    public void UpdateCollections(IEnumerable<string> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);

        var previousSelection = string.IsNullOrWhiteSpace(_selectedCollection) ? _noCollectionOption : _selectedCollection;
        var normalized = collections
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => !string.Equals(name, _noCollectionOption, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // A partial metadata snapshot must not discard a collection the user selected earlier.
        if (!string.Equals(previousSelection, _noCollectionOption, StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains(previousSelection, StringComparer.OrdinalIgnoreCase))
        {
            normalized.Add(previousSelection);
        }

        normalized.Insert(0, _noCollectionOption);
        var selection = normalized.First(option => string.Equals(option, previousSelection, StringComparison.OrdinalIgnoreCase));
        ReplaceCollectionOptions(normalized, selection);
    }

    private void ReplaceCollectionOptions(IReadOnlyList<string> options, string selection)
    {
        if (_collectionOptions.SequenceEqual(options, StringComparer.Ordinal))
        {
            SetProperty(ref _selectedCollection, selection, nameof(SelectedCollection));
            return;
        }

        var wasHydrating = _isHydrating;
        _isHydrating = true;
        try
        {
            // Invalidate the binding's cached source value before Reset, so restoring the same
            // collection name still reaches controls that lost SelectedItem during the rebuild.
            _selectedCollection = string.Empty;
            OnPropertyChanged(nameof(SelectedCollection));
            _collectionOptions.ReplaceWith(options);
            _selectedCollection = selection;
            // The control lost its selection during Reset even when the model value is unchanged.
            OnPropertyChanged(nameof(SelectedCollection));
        }
        finally
        {
            _isHydrating = wasHydrating;
        }
    }

    public void RefreshLocalization()
    {
        UpdateNoCollectionOption();
    }

    public void ResetFilters()
    {
        UpdatePreferences(p => p.Filters = new SelectionFilters());
    }

    private void UpdatePreferences(Action<SelectionPreferences> updater)
    {
        var preferences = _selectionEngine.GetPreferences();
        updater(preferences);
        try
        {
            _selectionEngine.UpdatePreferences(preferences);
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            LastSaveError = ex.Message;
        }
        var effective = _selectionEngine.GetPreferences();
        Apply(effective);
        PreferencesChanged?.Invoke(this, effective);
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

    private void UpdateStorefrontPreferences()
    {
        UpdatePreferences(p =>
        {
            p.Filters.FilterByStorefront = true;
            p.Filters.IncludedStorefronts = BuildSelectedStorefronts();
        });
    }

    private List<Storefront> BuildSelectedStorefronts()
    {
        var storefronts = new List<Storefront>();
        if (IncludeSteam)
        {
            storefronts.Add(Storefront.Steam);
        }

        return storefronts;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalization();
    }

    private void UpdateNoCollectionOption()
    {
        var previousNoCollection = _noCollectionOption;
        var previousSelection = _selectedCollection;

        var noCollection = _localizationService.GetString("Filters_NoCollection");
        if (_collectionOptions.Count > 0 && string.Equals(previousNoCollection, noCollection, StringComparison.Ordinal)) return;

        var isNoneSelected = string.IsNullOrWhiteSpace(previousSelection) ||
                             string.Equals(previousSelection, previousNoCollection, StringComparison.OrdinalIgnoreCase);

        var options = _collectionOptions.ToList();
        if (options.Count == 0)
        {
            options.Add(noCollection);
        }
        else
        {
            options[0] = noCollection;
        }

        var selection = isNoneSelected ? noCollection : previousSelection;
        if (!options.Contains(selection, StringComparer.Ordinal)) options.Add(selection);
        _noCollectionOption = noCollection;
        ReplaceCollectionOptions(options, selection);
    }
    public void Dispose() => _localizationService.LanguageChanged -= OnLanguageChanged;
}
