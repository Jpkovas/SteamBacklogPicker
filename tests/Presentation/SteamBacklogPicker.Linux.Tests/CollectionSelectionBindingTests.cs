using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Domain;
using Domain.Selection;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Localization;
using SteamBacklogPicker.UI.ViewModels;

namespace SteamBacklogPicker.Linux.Tests;

public sealed class CollectionSelectionBindingTests
{
    [AvaloniaFact]
    public void CollectionComboBox_ShouldPreserveSelectionThroughSnapshotsAndLanguageChanges()
    {
        var engine = new RecordingSelectionEngine();
        var localization = new LocalizationService();
        localization.SetLanguage("en-US");
        var viewModel = new SelectionPreferencesViewModel(engine, localization);
        viewModel.UpdateCollections(new[] { "Favorites", "VR" });
        var combo = new ComboBox { DataContext = viewModel };
        combo.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(viewModel.CollectionOptions)));
        combo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(viewModel.SelectedCollection)) { Mode = BindingMode.TwoWay });
        var window = new Window { Content = combo };
        window.Show();
        try
        {
            combo.SelectedItem.Should().Be(viewModel.CollectionOptions[0]);
            engine.WriteCount.Should().Be(0);
            combo.SelectedItem = "Favorites";
            engine.WriteCount.Should().Be(1);
            var changes = 0;
            ((INotifyCollectionChanged)viewModel.CollectionOptions).CollectionChanged += (_, _) => changes++;

            viewModel.UpdateCollections(new[] { "VR", "Favorites", "VR" });
            changes.Should().Be(0, "an unchanged incremental snapshot must not rebuild the ComboBox");
            combo.SelectedItem.Should().Be("Favorites");

            viewModel.UpdateCollections(new[] { "Favorites", "Co-op" });
            Dispatcher.UIThread.RunJobs();
            combo.SelectedItem.Should().Be("Favorites");
            viewModel.UpdateCollections(new[] { "New collection" });
            Dispatcher.UIThread.RunJobs();
            viewModel.CollectionOptions.Should().Contain("Favorites");
            combo.SelectedItem.Should().Be("Favorites");

            localization.SetLanguage("pt-BR");
            Dispatcher.UIThread.RunJobs();
            combo.SelectedItem.Should().Be("Favorites");
            viewModel.CollectionOptions[0].Should().Be(localization.GetString("Filters_NoCollection"));
            engine.WriteCount.Should().Be(1, "snapshots and translation are not preference edits");
            engine.GetPreferences().Filters.RequiredCollection.Should().Be("Favorites");

            combo.SelectedItem = viewModel.CollectionOptions[0];
            engine.WriteCount.Should().Be(2);
            localization.SetLanguage("en-US");
            viewModel.UpdateCollections(Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            combo.SelectedItem.Should().Be(localization.GetString("Filters_NoCollection"));
            viewModel.CollectionOptions.Should().ContainSingle();
            engine.GetPreferences().Filters.RequiredCollection.Should().BeNull();
            engine.WriteCount.Should().Be(2);
        }
        finally { window.Close(); }
    }

    private sealed class RecordingSelectionEngine : ISelectionEngine
    {
        private SelectionPreferences _preferences = new();
        public int WriteCount { get; private set; }
        public SelectionPreferences GetPreferences() => _preferences.Clone();
        public void UpdatePreferences(SelectionPreferences preferences)
        {
            _preferences = preferences.Clone();
            WriteCount++;
        }
        public IReadOnlyList<SelectionHistoryEntry> GetHistory() => Array.Empty<SelectionHistoryEntry>();
        public void ClearHistory() => throw new NotSupportedException();
        public GameEntry PickNext(IEnumerable<GameEntry> games) => throw new NotSupportedException();
        public IReadOnlyList<GameEntry> FilterGames(IEnumerable<GameEntry> games) => throw new NotSupportedException();
    }
}
