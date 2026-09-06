using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using QRCoder;
using QRCoder.Exceptions;
using SteamBacklogPicker.UI.ViewModels;

namespace SteamBacklogPicker.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel; DataContext = viewModel;
        Loaded += OnLoaded;
        viewModel.PropertyChanged += OnViewModelChanged;
        PreviewKeyDown += OnKeyDown;
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try { await _viewModel.InitializeAsync(); }
        catch (Exception ex) { _viewModel.StatusMessage = ex.Message; }
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.QrChallengeUrl)) return;
        LoginQrImage.Source = CreateLoginQrImage(_viewModel.QrChallengeUrl);
    }
    internal static BitmapSource? CreateLoginQrImage(string? url)
    {
        if (url is not { Length: > 0 and < 4096 }) return null;
        // The ephemeral challenge is rendered in memory and never written to disk or diagnostics.
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qr = new PngByteQRCode(data);
            using var input = new MemoryStream(qr.GetGraphic(8));
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit();
            bitmap.Freeze(); return bitmap;
        }
        catch (Exception ex) when (ex is DataTooLongException or ArgumentException or IOException or NotSupportedException)
        { return null; }
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.NavigateCommand.Execute("library"); SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            if (_viewModel.RefreshCommand.CanExecute(null)) _viewModel.RefreshCommand.Execute(null); e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            var hadFilters = _viewModel.AdvancedFilters;
            _viewModel.AdvancedFilters = false;
            if (_viewModel.IsConnecting) _viewModel.CancelSyncCommand.Execute(null);
            if (hadFilters) { FiltersButton.Focus(); e.Handled = true; }
        }
    }
    private void OnBacklogMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.Dispose();
        LoginQrImage.Source = null;
        base.OnClosed(e);
    }
}
