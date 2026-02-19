using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SteamBacklogPicker.UI.ViewModels;

namespace SteamBacklogPicker.Linux.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _ = viewModel.InitializeAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
