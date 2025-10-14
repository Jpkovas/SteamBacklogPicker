using System;
using System.Collections.Generic;
using Domain;
using Domain.Selection;
using FluentAssertions;
using SteamBacklogPicker.UI.Services;
using SteamBacklogPicker.UI.ViewModels;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class SelectionPreferencesViewModelTests
{
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

    private sealed class FakeSelectionEngine : ISelectionEngine
    {
        private SelectionPreferences _preferences;

        public FakeSelectionEngine(SelectionPreferences initialPreferences)
        {
            _preferences = initialPreferences?.Clone() ?? throw new ArgumentNullException(nameof(initialPreferences));
            LastUpdatedPreferences = _preferences.Clone();
        }

        public SelectionPreferences LastUpdatedPreferences { get; private set; }

        public SelectionPreferences GetPreferences() => _preferences.Clone();

        public void UpdatePreferences(SelectionPreferences preferences)
        {
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

        public string CurrentLanguage { get; private set; } = "pt-BR";

        public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "en-US", "pt-BR" };

        public void SetLanguage(string languageCode)
        {
            CurrentLanguage = languageCode;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key) => key switch
        {
            "Filters_NoCollection" => "Nenhuma coleção",
            _ => key,
        };

        public string GetString(string key, params object[] arguments) => string.Format(GetString(key), arguments);

        public string FormatGameCount(int count) => count.ToString();
    }
}
