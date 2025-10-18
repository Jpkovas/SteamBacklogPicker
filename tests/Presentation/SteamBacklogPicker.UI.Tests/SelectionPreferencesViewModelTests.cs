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
    public void DeckCompatibilityPreferences_ShouldUpdateSelectionEngine()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                AllowedDeckCompatibility = DeckCompatibilityFilter.All,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.IncludeDeckUnsupported = false;
        engine.LastUpdatedPreferences.Filters.AllowedDeckCompatibility.Should().Be(DeckCompatibilityFilter.All & ~DeckCompatibilityFilter.Unsupported);
        viewModel.IncludeDeckUnsupported.Should().BeFalse();

        viewModel.IncludeDeckVerified = false;
        engine.LastUpdatedPreferences.Filters.AllowedDeckCompatibility.Should().Be(DeckCompatibilityFilter.Unknown | DeckCompatibilityFilter.Playable);
        viewModel.IncludeDeckVerified.Should().BeFalse();

        viewModel.IncludeDeckUnknown = false;
        engine.LastUpdatedPreferences.Filters.AllowedDeckCompatibility.Should().Be(DeckCompatibilityFilter.Playable);
        viewModel.IncludeDeckUnknown.Should().BeFalse();

        viewModel.IncludeDeckPlayable = false;
        engine.LastUpdatedPreferences.Filters.AllowedDeckCompatibility.Should().Be(DeckCompatibilityFilter.None);
        viewModel.IncludeDeckPlayable.Should().BeFalse();
    }

    [Fact]
    public void RecentGameExclusionCount_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            RecentGameExclusionCount = 2,
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.RecentGameExclusionCount.Should().Be(2);

        viewModel.RecentGameExclusionCount = 4;

        engine.LastUpdatedPreferences.RecentGameExclusionCount.Should().Be(4);
        viewModel.RecentGameExclusionCount.Should().Be(4);
    }

    [Fact]
    public void InstallStateWeight_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                InstallStateWeight = 1d,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.InstallStateWeight = 1.6d;

        engine.LastUpdatedPreferences.Filters.InstallStateWeight.Should().Be(1.6d);
        viewModel.InstallStateWeight.Should().Be(1.6d);
    }

    [Fact]
    public void LastPlayedWeight_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                LastPlayedRecencyWeight = 1d,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.LastPlayedWeight = 1.8d;

        engine.LastUpdatedPreferences.Filters.LastPlayedRecencyWeight.Should().Be(1.8d);
        viewModel.LastPlayedWeight.Should().Be(1.8d);
    }

    [Fact]
    public void DeckCompatibilityWeight_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                DeckCompatibilityWeight = 1d,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.DeckCompatibilityWeight = 0.4d;

        engine.LastUpdatedPreferences.Filters.DeckCompatibilityWeight.Should().Be(0.4d);
        viewModel.DeckCompatibilityWeight.Should().Be(0.4d);
    }

    [Fact]
    public void RequireSinglePlayer_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                RequireSinglePlayer = false,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.RequireSinglePlayer = true;

        engine.LastUpdatedPreferences.Filters.RequireSinglePlayer.Should().BeTrue();
        viewModel.RequireSinglePlayer.Should().BeTrue();
    }

    [Fact]
    public void RequireMultiplayer_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                RequireMultiplayer = false,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.RequireMultiplayer = true;

        engine.LastUpdatedPreferences.Filters.RequireMultiplayer.Should().BeTrue();
        viewModel.RequireMultiplayer.Should().BeTrue();
    }

    [Fact]
    public void RequireVr_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                RequireVr = false,
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.RequireVr = true;

        engine.LastUpdatedPreferences.Filters.RequireVr.Should().BeTrue();
        viewModel.RequireVr.Should().BeTrue();
    }

    [Fact]
    public void MoodTagsText_ShouldUpdatePreferences()
    {
        var initialPreferences = new SelectionPreferences
        {
            Filters = new SelectionFilters
            {
                MoodTags = new List<string>(),
            },
        };

        var engine = new FakeSelectionEngine(initialPreferences);
        var localization = new FakeLocalizationService();
        var viewModel = new SelectionPreferencesViewModel(engine, localization);

        viewModel.MoodTagsText = "Relaxing, Story-Rich, relaxing";

        engine.LastUpdatedPreferences.Filters.MoodTags.Should().Contain(new[] { "Relaxing", "Story-Rich" });
        viewModel.MoodTagsText.Should().Be("Relaxing, Story-Rich");
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
            "Filters_RecentExclusionLabel" => "Sorteios recentes",
            "Filters_RecentExclusion_HelpText" => "Quantidade de sorteios recentes a ignorar",
            "Filters_RecentExclusion_ValuePrefix" => "Ignorar últimos:",
            "Filters_WeightsLabel" => "Pesos",
            "Filters_InstallWeightLabel" => "Instalação",
            "Filters_InstallWeight_HelpText" => "Preferir instalados",
            "Filters_LastPlayedWeightLabel" => "Última vez jogado",
            "Filters_LastPlayedWeight_HelpText" => "Preferir não jogados",
            "Filters_DeckWeightLabel" => "Compatibilidade Deck",
            "Filters_DeckWeight_HelpText" => "Preferir compatíveis",
            _ => key,
        };

        public string GetString(string key, params object[] arguments) => string.Format(GetString(key), arguments);

        public string FormatGameCount(int count) => count.ToString();
    }
}
