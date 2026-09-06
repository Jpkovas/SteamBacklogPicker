using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using SteamBacklogPicker.UI.Services.GameArt;

namespace SteamBacklogPicker.UI;

/// <summary>Loads only realized artwork and releases row references when recycled.</summary>
public sealed class CachedArtwork : Image
{
    private static readonly ArtworkCache Cache = new();
    private static readonly DecodedArtworkCache Bitmaps = new();
    private CancellationTokenSource? _load;
    private int _requestedWidth;
    private string? _requestedPath;
    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(nameof(SourcePath), typeof(string),
        typeof(CachedArtwork), new PropertyMetadata(null, OnInputChanged));
    public static readonly DependencyProperty AllowNetworkProperty = DependencyProperty.Register(nameof(AllowNetwork), typeof(bool),
        typeof(CachedArtwork), new PropertyMetadata(false, OnInputChanged));
    public static readonly DependencyProperty DecodePixelWidthProperty = DependencyProperty.Register(nameof(DecodePixelWidth), typeof(int),
        typeof(CachedArtwork), new PropertyMetadata(0, OnInputChanged));
    public string? SourcePath { get => (string?)GetValue(SourcePathProperty); set => SetValue(SourcePathProperty, value); }
    public bool AllowNetwork { get => (bool)GetValue(AllowNetworkProperty); set => SetValue(AllowNetworkProperty, value); }
    public int DecodePixelWidth { get => (int)GetValue(DecodePixelWidthProperty); set => SetValue(DecodePixelWidthProperty, value); }

    public CachedArtwork()
    {
        Loaded += (_, _) => LoadImage();
        Unloaded += (_, _) =>
        {
            _load?.Cancel(); _load?.Dispose(); _load = null;
            Source = null; _requestedPath = null; _requestedWidth = 0;
        };
        SizeChanged += (_, _) => { if (IsLoaded && RequestedWidth() != _requestedWidth) LoadImage(); };
    }
    private int RequestedWidth()
    {
        var pixels = DecodePixelWidth > 0 ? DecodePixelWidth : Math.Max(ActualWidth, 160) * VisualTreeHelper.GetDpi(this).DpiScaleX;
        return pixels <= 160 ? 160 : pixels <= 320 ? 320 : pixels <= 640 ? 640 : 1200;
    }
    private static void OnInputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    { if (sender is CachedArtwork { IsLoaded: true } image) image.LoadImage(); }
    private async void LoadImage()
    {
        _load?.Cancel(); _load?.Dispose();
        _load = new CancellationTokenSource(); var token = _load.Token;
        var path = SourcePath; var network = AllowNetwork; var width = RequestedWidth();
        if (_requestedPath != path) Source = null;
        _requestedPath = path; _requestedWidth = width;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var bitmap = await Bitmaps.GetAsync(CacheKey(path), width, cancellation => Cache.GetAsync(path, network, cancellation), token);
            if (bitmap is null && path.Contains("/library_hero.jpg", StringComparison.Ordinal))
            {
                var header = path.Replace("/library_hero.jpg", "/header.jpg", StringComparison.Ordinal);
                bitmap = await Bitmaps.GetAsync(CacheKey(header), width, cancellation => Cache.GetAsync(header, network, cancellation), token);
            }
            if (!token.IsCancellationRequested && IsLoaded && SourcePath == path) Source = bitmap;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or System.Net.Http.HttpRequestException
            or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
        { /* Failed artwork leaves game actions available. */ }
    }
    private static string CacheKey(string path)
    {
        if (!File.Exists(path)) return path;
        var file = new FileInfo(path);
        return path + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
    }
}
