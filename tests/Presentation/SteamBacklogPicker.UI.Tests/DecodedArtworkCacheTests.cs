using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace SteamBacklogPicker.UI.Tests;

public sealed class DecodedArtworkCacheTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Decode_ShouldUseThumbnailSizeAndShareFrozenImagesAcrossConsumers()
    {
        var png = await OnSta(() => CreatePng(1920, 1080));
        var cache = new DecodedArtworkCache();
        var loads = 0;
        Task<byte[]?> Load(CancellationToken _) { Interlocked.Increment(ref loads); return Task.FromResult<byte[]?>(png); }
        var watch = Stopwatch.StartNew();
        var images = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => cache.GetAsync("same", 160, Load, default)));
        watch.Stop();
        var thumbnail = images[0]!;
        images.Should().OnlyContain(image => ReferenceEquals(image, thumbnail));
        thumbnail.IsFrozen.Should().BeTrue();
        thumbnail.PixelWidth.Should().Be(160);
        thumbnail.PixelHeight.Should().Be(90);
        cache.DecodeCount.Should().Be(1);
        loads.Should().Be(1);
        var hero = await cache.GetAsync("same", 1200, Load, default);
        hero!.PixelWidth.Should().Be(1200);
        hero.PixelHeight.Should().Be(675);
        output.WriteLine($"consumers=60; elapsed_ms={watch.Elapsed.TotalMilliseconds:F3}; thumbnail_decodes=1; thumbnail_bytes={160 * 90 * 4}; previous_1200px_bytes={1200 * 675 * 4}; retained_cache_bytes={cache.EstimatedBytes}");
    }

    [Fact]
    public async Task Cache_ShouldEvictLeastRecentlyUsedImageWithinByteBudget()
    {
        var png = await OnSta(() => CreatePng(320, 180));
        var cache = new DecodedArtworkCache(maxBytes: 2 * 160 * 90 * 4, maxEntries: 2);
        Task<byte[]?> Load(CancellationToken _) => Task.FromResult<byte[]?>(png);
        var a = await cache.GetAsync("a", 160, Load, default);
        var b = await cache.GetAsync("b", 160, Load, default);
        (await cache.GetAsync("a", 160, Load, default)).Should().BeSameAs(a);
        await cache.GetAsync("c", 160, Load, default);
        cache.Count.Should().Be(2);
        cache.EstimatedBytes.Should().Be(115200);
        (await cache.GetAsync("a", 160, Load, default)).Should().BeSameAs(a);
        (await cache.GetAsync("b", 160, Load, default)).Should().NotBeSameAs(b);
        cache.DecodeCount.Should().Be(4);
    }

    [Fact]
    public async Task Decode_ShouldRejectCorruptImagesWithoutCachingOrThrowing()
    {
        var cache = new DecodedArtworkCache();
        var result = await cache.GetAsync("broken", 160, _ => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 }), default);
        result.Should().BeNull();
        cache.Count.Should().Be(0);
        cache.EstimatedBytes.Should().Be(0);
    }

    [Fact]
    public async Task Unloaded_ShouldReleaseImageSourceForRecycledControls()
    {
        await OnSta(() =>
        {
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            bitmap.Freeze();
            var artwork = new CachedArtwork { Source = bitmap };
            artwork.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            artwork.Source.Should().BeNull();
            return true;
        });
    }

    private static byte[] CreatePng(int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Task<T> OnSta<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { result.SetResult(action()); }
            catch (Exception ex) { result.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
