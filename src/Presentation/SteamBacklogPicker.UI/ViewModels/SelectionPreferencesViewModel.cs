using System;
using System.Collections.Generic;
using System.Linq;
using Domain.Selection;

namespace SteamBacklogPicker.UI.ViewModels;

public sealed class SelectionPreferencesViewModel : ObservableObject
{
    private readonly ISelectionEngine _selectionEngine;
    private bool _requireInstalled;
    private bool _includeFamilyShared = true;
    private string? _requiredTags;
    private string? _minimumSizeGb;
    private string? _maximumSizeGb;
    private bool _isHydrating;

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

    public bool IncludeFamilyShared
    {
        get => _includeFamilyShared;
        set
        {
            if (SetProperty(ref _includeFamilyShared, value) && !_isHydrating)
            {
                UpdatePreferences(p => p.Filters.IncludeFamilyShared = value);
            }
        }
    }

    public string? RequiredTags
    {
        get => _requiredTags;
        set
        {
            if (SetProperty(ref _requiredTags, value) && !_isHydrating)
            {
                UpdatePreferences(p =>
                {
                    p.Filters.RequiredTags = ParseTags(value);
                });
            }
        }
    }

    public string? MinimumSizeGb
    {
        get => _minimumSizeGb;
        set
        {
            if (SetProperty(ref _minimumSizeGb, value) && !_isHydrating)
            {
                UpdatePreferences(p => p.Filters.MinimumSizeOnDisk = ParseSize(value));
            }
        }
    }

    public string? MaximumSizeGb
    {
        get => _maximumSizeGb;
        set
        {
            if (SetProperty(ref _maximumSizeGb, value) && !_isHydrating)
            {
                UpdatePreferences(p => p.Filters.MaximumSizeOnDisk = ParseSize(value));
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
            IncludeFamilyShared = preferences.Filters.IncludeFamilyShared;
            RequiredTags = string.Join(", ", preferences.Filters.RequiredTags ?? Enumerable.Empty<string>());
            MinimumSizeGb = FormatSize(preferences.Filters.MinimumSizeOnDisk);
            MaximumSizeGb = FormatSize(preferences.Filters.MaximumSizeOnDisk);
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

    private static List<string> ParseTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<string>();
        }

        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long? ParseSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, out var gigabytes) && gigabytes > 0)
        {
            return (long)(gigabytes * 1024 * 1024 * 1024);
        }

        return null;
    }

    private static string? FormatSize(long? bytes)
    {
        if (bytes is null || bytes <= 0)
        {
            return null;
        }

        var gigabytes = bytes.Value / (1024d * 1024d * 1024d);
        return gigabytes.ToString("0.##");
    }
}
