using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

[assembly: InternalsVisibleTo("SteamBacklogPicker.UI.Tests")]

namespace SteamBacklogPicker.UI;

internal sealed class DecodedArtworkCache(long maxBytes = 64L * 1024 * 1024, int maxEntries = 128)
{
    private sealed record Entry(string Key, BitmapSource Bitmap, long Bytes);
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recent = new();
    private readonly SemaphoreSlim _decoders = new(2);
    private readonly SemaphoreSlim[] _keys = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1)).ToArray();
    private long _bytes;
    private int _decodeCount;
    internal int Count { get { lock (_sync) return _entries.Count; } }
    internal long EstimatedBytes { get { lock (_sync) return _bytes; } }
    internal int DecodeCount => Volatile.Read(ref _decodeCount);

    internal async Task<BitmapSource?> GetAsync(string source, int width, Func<CancellationToken, Task<byte[]?>> load, CancellationToken token)
    {
        var key = source + "|" + width;
        var gate = _keys[(StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % _keys.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_entries.TryGetValue(key, out var cached))
                {
                    _recent.Remove(cached); _recent.AddFirst(cached);
                    return cached.Value.Bitmap;
                }
            }
            var data = await load(token).ConfigureAwait(false);
            if (data is null) return null;
            await _decoders.WaitAsync(token).ConfigureAwait(false);
            BitmapSource? bitmap;
            try
            {
                bitmap = await Task.Run(() => Decode(data, width), token).ConfigureAwait(false);
                Interlocked.Increment(ref _decodeCount);
            }
            finally { _decoders.Release(); }
            token.ThrowIfCancellationRequested();
            if (bitmap is null) return null;
            var bytes = (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
            lock (_sync)
            {
                if (bytes > maxBytes) return bitmap;
                while (_recent.Last is { } last && (_bytes + bytes > maxBytes || _entries.Count >= maxEntries))
                {
                    _entries.Remove(last.Value.Key); _recent.RemoveLast(); _bytes -= last.Value.Bytes;
                }
                var node = _recent.AddFirst(new Entry(key, bitmap, bytes));
                _entries[key] = node; _bytes += bytes;
            }
            return bitmap;
        }
        finally { gate.Release(); }
    }

    private static BitmapSource? Decode(byte[] bytes, int width)
    {
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            var pixels = (long)frame.PixelWidth * frame.PixelHeight;
            if (pixels > 64_000_000 || frame.PixelWidth <= 0 || frame.PixelHeight <= 0) return null;
            var targetWidth = Math.Clamp(width, 1, Math.Min(1200, frame.PixelWidth));
            if ((long)frame.PixelHeight * targetWidth / frame.PixelWidth > 4096)
                targetWidth = Math.Max(1, 4096 * frame.PixelWidth / frame.PixelHeight);
            input.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = targetWidth; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
        { return null; }
    }
}
