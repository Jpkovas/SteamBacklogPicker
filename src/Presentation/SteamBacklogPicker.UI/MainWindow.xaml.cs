using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SteamBacklogPicker.UI.ViewModels;

namespace SteamBacklogPicker.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = ex.Message;
        }
    }

    private void OnCoverImageTargetUpdated(object sender, DataTransferEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        image.Tag = null;
        image.Visibility = Visibility.Visible;
        ApplyRoundedClip(image.Parent as Border);
    }

    private void OnCoverImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        var appId = _viewModel.SelectedGame.AppId;
        if (appId == 0)
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            return;
        }

        var nextStage = GetNextFallbackStage(image.Tag as string);
        if (nextStage is null)
        {
            image.Tag = "exhausted";
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            return;
        }

        var nextUri = BuildCoverUri(nextStage, appId);
        if (!Uri.TryCreate(nextUri, UriKind.Absolute, out var uri))
        {
            image.Tag = "exhausted";
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            return;
        }

        image.Tag = nextStage;
        image.Source = CreateBitmap(uri);
        image.Visibility = Visibility.Visible;
        ApplyRoundedClip(image.Parent as Border);
        e.Handled = true;
    }

    private static string? GetNextFallbackStage(string? currentStage) => currentStage switch
    {
        null => "header",
        "header" => "capsule",
        "capsule" => "steamdb",
        "steamdb" => "portrait",
        _ => null,
    };

    private static string BuildCoverUri(string stage, uint appId) => stage switch
    {
        "header" => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
        "capsule" => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/capsule_616x353.jpg",
        "steamdb" => $"https://steamdb.info/static/cdn/steam/apps/{appId}/header.jpg",
        "portrait" => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
        _ => string.Empty,
    };

    private static BitmapImage CreateBitmap(Uri uri)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = uri;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        return bitmap;
    }

    private void OnCoverContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border)
        {
            ApplyRoundedClip(border);
        }
    }

    private static void ApplyRoundedClip(Border? border)
    {
        if (border is null)
        {
            return;
        }

        var width = border.ActualWidth;
        var height = border.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            border.Clip = null;
            return;
        }

        border.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 16, 16);
    }
}
