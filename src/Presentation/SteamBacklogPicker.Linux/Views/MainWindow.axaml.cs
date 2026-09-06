using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using QRCoder;
using SteamBacklogPicker.Linux.Controls;
using SteamBacklogPicker.UI.ViewModels;

namespace SteamBacklogPicker.Linux.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private Bitmap? _qrBitmap;
    private string? _renderedQrChallenge;
    private readonly CancellationTokenSource _viewLifetime = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        KeyDown += OnKeyDown;
    }

    public MainWindow(MainViewModel viewModel) : this() => DataContext = viewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
        UpdateQrCode();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _ = ApplyMotionPreferenceAsync();
        if (_viewModel is null) return;
        try { await _viewModel.InitializeAsync(); }
        catch (Exception ex) { _viewModel.StatusMessage = ex.Message; }
    }

    private async Task ApplyMotionPreferenceAsync()
    {
        var token = _viewLifetime.Token;
        var reduced = await MotionPreferences.PrefersReducedMotionAsync(token);
        if (!token.IsCancellationRequested) Classes.Set("motion-enabled", !reduced);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.QrChallengeUrl)) UpdateQrCode();
        if (e.PropertyName == nameof(MainViewModel.ActivePage) && _viewModel is { } viewModel)
            viewModel.AdvancedFilters = false;
    }

    private void UpdateQrCode()
    {
        var challenge = _viewModel?.QrChallengeUrl;
        if (string.Equals(challenge, _renderedQrChallenge, StringComparison.Ordinal)) return;
        _renderedQrChallenge = challenge;
        LoginQrImage.Source = null;
        _qrBitmap?.Dispose();
        _qrBitmap = null;
        if (challenge is not { Length: > 0 and < 4096 } url) return;
        // A login challenge is ephemeral: render in memory, never via the disk artwork cache.
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qr = new PngByteQRCode(data);
            using var input = new MemoryStream(qr.GetGraphic(8));
            _qrBitmap = new Bitmap(input);
            LoginQrImage.Source = _qrBitmap;
        }
        catch (QRCoder.Exceptions.DataTooLongException)
        {
            // A character limit alone does not guarantee that UTF-8 data fits QR capacity.
            // Leave the connection controls available so the user can request a new challenge.
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel) return;
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.NavigateCommand.Execute("library");
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            if (viewModel.RefreshCommand.CanExecute(null)) viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.AdvancedFilters = false;
            if (viewModel.IsSyncBusy) viewModel.CancelSyncCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewLifetime.Cancel();
        _viewLifetime.Dispose();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.Dispose();
        }
        LoginQrImage.Source = null;
        _qrBitmap?.Dispose();
        _qrBitmap = null;
        base.OnClosed(e);
        _renderedQrChallenge = null;
    }
}
