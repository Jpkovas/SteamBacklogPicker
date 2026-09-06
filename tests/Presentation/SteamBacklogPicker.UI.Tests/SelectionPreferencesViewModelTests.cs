using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Domain;
using Domain.Selection;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.ViewModels;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class SelectionPreferencesViewModelTests
{
    [Fact]
    public void FailedPreferenceWrite_ShouldRestoreEffectiveFilterAndReportError()
    {
        var engine = new FakeSelectionEngine(new SelectionPreferences()) { WriteError = new System.IO.IOException("Fixture disk unavailable") };
        using var viewModel = new SelectionPreferencesViewModel(engine, new FakeLocalizationService());
        var notifications = 0;
        viewModel.PreferencesChanged += (_, _) => notifications++;
        viewModel.RequireInstalled = true;
        viewModel.RequireInstalled.Should().BeFalse();
        viewModel.LastSaveError.Should().Be("Fixture disk unavailable");
        engine.GetPreferences().Filters.RequireInstalled.Should().BeFalse();
        notifications.Should().Be(1);
        engine.WriteError = null;
        viewModel.RequireInstalled = true;
        viewModel.RequireInstalled.Should().BeTrue();
        viewModel.LastSaveError.Should().BeNull();
    }

    [Fact]
    public void SelectedCollection_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                RequireInstalled = false,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.UpdateCollections(new[] { "Jogáveis no Deck", "Multijogador" });

        var noneLabel = viewModel.CollectionOptions[0];
        viewModel.SelectedCollection.Should().Be(noneLabel);

        viewModel.SelectedCollection = "Jogáveis no Deck";
        engine.LastUpdatedPreferences.Filters.RequiredCollection.Should().Be("Jogáveis no Deck");

        viewModel.SelectedCollection = noneLabel;
        engine.LastUpdatedPreferences.Filters.RequiredCollection.Should().BeNull();
    }

    [Fact]
    public void UpdateCollections_ShouldKeepExistingSelectionWhenAvailable()
    {
        var engine = new FakeSelectionEngine(new SelectionPreferences());
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);
        viewModel.UpdateCollections(new[] { "VR", "Favoritos" });

        viewModel.SelectedCollection = "VR";
        viewModel.UpdateCollections(new[] { "VR", "Multijogador" });

        viewModel.SelectedCollection.Should().Be("VR");
        viewModel.CollectionOptions.Should().Contain("Nenhuma coleção");
        viewModel.CollectionOptions.Should().Contain("VR");
        viewModel.CollectionOptions.Should().Contain("Multijogador");
    }

    [Fact]
    public void CollectionRefresh_ShouldIgnoreTransientTwoWayNullAndAvoidUnchangedWrites()
    {
        var engine = new FakeSelectionEngine(new SelectionPreferences());
        var localization = new LocalizationService();
        localization.SetLanguage("en-US");
        var viewModel = new SelectionPreferencesViewModel(engine, localization);
        viewModel.UpdateCollections(new[] { "Favorites", "VR" });
        engine.UpdateCount.Should().Be(0);
        viewModel.SelectedCollection = "Favorites";

        var collectionChanges = 0;
        var selectionNotifications = 0;
        ((INotifyCollectionChanged)viewModel.CollectionOptions).CollectionChanged += (_, _) =>
        {
            collectionChanges++;
            // WPF may return a cleared selection while its ItemsSource processes Reset/Replace.
            viewModel.SelectedCollection = null!;
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.SelectedCollection)) selectionNotifications++;
        };

        viewModel.UpdateCollections(new[] { "VR", "Favorites" });
        collectionChanges.Should().Be(0);
        selectionNotifications.Should().Be(0);
        viewModel.UpdateCollections(new[] { "Co-op" });
        collectionChanges.Should().Be(1, "the replacement should publish a single reset");
        viewModel.CollectionOptions.Should().Contain("Favorites");
        viewModel.SelectedCollection.Should().Be("Favorites");
        selectionNotifications.Should().BeGreaterThan(0, "the control must reapply its selection after a reset");
        localization.SetLanguage("pt-BR");
        viewModel.SelectedCollection.Should().Be("Favorites");
        engine.UpdateCount.Should().Be(1);
        engine.LastUpdatedPreferences.Filters.RequiredCollection.Should().Be("Favorites");

        viewModel.SelectedCollection = viewModel.CollectionOptions[0];
        localization.SetLanguage("en-US");
        viewModel.UpdateCollections(Array.Empty<string>());
        viewModel.SelectedCollection.Should().Be(localization.GetString("Filters_NoCollection"));
        engine.UpdateCount.Should().Be(2);
        engine.LastUpdatedPreferences.Filters.RequiredCollection.Should().BeNull();
    }

    [Fact]
    public void Dispose_ShouldDetachLocalizationSubscription()
    {
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(new FakeSelectionEngine(new SelectionPreferences()), localization);
        viewModel.Dispose();
        var notifications = 0;
        viewModel.PropertyChanged += (_, _) => notifications++;
        localization.SetLanguage("en-US");
        notifications.Should().Be(0);
    }

    [Fact]
    public void ExcludeDeckUnsupported_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                ExcludeDeckUnsupported = false,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.ExcludeDeckUnsupported = true;
        engine.LastUpdatedPreferences.Filters.ExcludeDeckUnsupported.Should().BeTrue();
        viewModel.ExcludeDeckUnsupported.Should().BeTrue();

        viewModel.ExcludeDeckUnsupported = false;
        engine.LastUpdatedPreferences.Filters.ExcludeDeckUnsupported.Should().BeFalse();
        viewModel.ExcludeDeckUnsupported.Should().BeFalse();
    }

    [Fact]
    public void RequireMacCompatible_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                RequireMacCompatible = false,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.RequireMacCompatible = true;
        engine.LastUpdatedPreferences.Filters.RequireMacCompatible.Should().BeTrue();
        viewModel.RequireMacCompatible.Should().BeTrue();

        viewModel.RequireMacCompatible = false;
        engine.LastUpdatedPreferences.Filters.RequireMacCompatible.Should().BeFalse();
        viewModel.RequireMacCompatible.Should().BeFalse();
    }

    [Fact]
    public void StorefrontToggles_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                IncludedStorefronts = new List<Storefront> { Storefront.Steam },
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.IncludeSteam.Should().BeTrue();

        viewModel.IncludeSteam = false;
        engine.LastUpdatedPreferences.Filters.FilterByStorefront.Should().BeTrue();
        engine.LastUpdatedPreferences.Filters.IncludedStorefronts.Should().BeEmpty();
        viewModel.IncludeSteam.Should().BeFalse();
    }

    private sealed class FakeSelectionEngine : ISelectionEngine
    {
        private SelectionPreferences _preferences;

        public FakeSelectionEngine(SelectionPreferences initialPreferences)
        {
            _preferences = initialPreferences?.Clone() ?? throw new ArgumentNullException(nameof(initialPreferences));
            LastUpdatedPreferences = _preferences.Clone();
        }

        public SelectionPreferences LastUpdatedPreferences { get; private set; }

        public int UpdateCount { get; private set; }
        public Exception? WriteError { get; set; }

        public SelectionPreferences GetPreferences() => _preferences.Clone();

        public void UpdatePreferences(SelectionPreferences preferences)
        {
            if (WriteError is not null) throw WriteError;
            UpdateCount++;
            LastUpdatedPreferences = preferences.Clone();
            _preferences = preferences.Clone();
        }

        public IReadOnlyList<SelectionHistoryEntry> GetHistory() => Array.Empty<SelectionHistoryEntry>();

        public void ClearHistory()
        {
        }

        public GameEntry PickNext(IEnumerable<GameEntry> games) => throw new NotSupportedException();

        public IReadOnlyList<GameEntry> FilterGames(IEnumerable<GameEntry> games) => throw new NotSupportedException();
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public event EventHandler? LanguageChanged;

        public event EventHandler<IReadOnlyDictionary<string, string>>? ResourcesChanged;

        public string CurrentLanguage { get; private set; } = "pt-BR";

        public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "en-US", "pt-BR" };

        public void SetLanguage(string languageCode)
        {
            CurrentLanguage = languageCode;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
            ResourcesChanged?.Invoke(this, GetAllStrings());
        }

        public string GetString(string key) => key switch
        {
            "Filters_NoCollection" => "Nenhuma coleção",
            _ => key,
        };

        public string GetString(string key, params object[] arguments) => string.Format(GetString(key), arguments);

        public string FormatGameCount(int count) => count.ToString();

        public IReadOnlyDictionary<string, string> GetAllStrings() => new Dictionary<string, string>();
    }
}
