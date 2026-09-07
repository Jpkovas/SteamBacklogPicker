using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SteamBacklogPicker.UI.Services.GameArt;

namespace SteamBacklogPicker.Linux.Controls;

/// <summary>Loads bounded artwork asynchronously and cancels when virtualized rows are recycled.</summary>
public sealed class CachedArtwork : Image
{
    private static readonly ArtworkCache Cache = new();
    private CancellationTokenSource? _load;
    private Bitmap? _bitmap;
    private bool _attached;

    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<CachedArtwork, string?>(nameof(SourcePath));
    public static readonly StyledProperty<bool> AllowNetworkProperty =
        AvaloniaProperty.Register<CachedArtwork, bool>(nameof(AllowNetwork));

    public string? SourcePath { get => GetValue(SourcePathProperty); set => SetValue(SourcePathProperty, value); }
    public bool AllowNetwork { get => GetValue(AllowNetworkProperty); set => SetValue(AllowNetworkProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_attached && (change.Property == SourcePathProperty || change.Property == AllowNetworkProperty))
            LoadImage();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        LoadImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        CancelLoad();
        ReplaceBitmap(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void CancelLoad()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
    }

    private void ReplaceBitmap(Bitmap? bitmap)
    {
        var previous = _bitmap;
        _bitmap = bitmap;
        Source = bitmap;
        previous?.Dispose();
    }

    private async void LoadImage()
    {
        CancelLoad();
        _load = new CancellationTokenSource();
        var token = _load.Token;
        var path = SourcePath;
        var allowNetwork = AllowNetwork;
        ReplaceBitmap(null);
        try
        {
            var bytes = await Cache.GetAsync(path, allowNetwork, token);
            if (bytes is null && path?.Contains("/library_hero.jpg", StringComparison.Ordinal) == true)
                bytes = await Cache.GetAsync(path.Replace("/library_hero.jpg", "/header.jpg", StringComparison.Ordinal), allowNetwork, token);
            if (bytes is null || token.IsCancellationRequested) return;
            var bitmap = await Task.Run(() =>
            {
                using var input = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(input, 1200);
            }, token);
            if (!token.IsCancellationRequested && _attached) ReplaceBitmap(bitmap);
            else bitmap.Dispose();
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or System.Net.Http.HttpRequestException
            or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Artwork failure must leave titles and actions usable.
        }
    }
}
