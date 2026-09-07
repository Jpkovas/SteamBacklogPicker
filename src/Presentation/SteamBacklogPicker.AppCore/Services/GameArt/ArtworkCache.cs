using System.Net.Http;
using System.Security.Cryptography;

namespace SteamBacklogPicker.UI.Services.GameArt;

/// <summary>Bounded, app-owned artwork cache. It never writes into Steam directories.</summary>
public sealed class ArtworkCache
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly SemaphoreSlim Downloads = new(4);
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1)).ToArray();
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly string _directory;
    private readonly HttpClient _client;
    private readonly object _failureSync = new(), _diskSync = new();
    private readonly Dictionary<string, DateTimeOffset> _failures = new(StringComparer.Ordinal);
    private Dictionary<string, (long Length, DateTime Modified)>? _diskEntries;
    public ArtworkCache(string? directory = null, HttpClient? client = null)
    {
        _client = client ?? Client;
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamBacklogPicker", "artwork");
    }
    public async Task<byte[]?> GetAsync(string? source, bool allowNetwork, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (File.Exists(source))
        {
            return await ReadFileAsync(source, token).ConfigureAwait(false);
        }
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !AllowedHost(uri.Host)) return null;
        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
        var path = Path.Combine(_directory, key + ".img");
        var gate = Locks[(key[0] + key[1]) % Locks.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                // Serve cached art immediately, including while offline.
                var cached = await ReadFileAsync(path, token).ConfigureAwait(false);
                if (cached is not null) return cached;
            }
            if (!allowNetwork || IsBackingOff(key)) return null;
            await Downloads.WaitAsync(token).ConfigureAwait(false);
            byte[] bytes;
            try
            {
                using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes ||
                    response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png" or "image/webp"))
                {
                    MarkFailure(key);
                    return null;
                }
                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var downloaded = await ReadBoundedAsync(stream, token).ConfigureAwait(false);
                if (downloaded is not { Length: > 0 }) { MarkFailure(key); return null; }
                bytes = downloaded;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { MarkFailure(key); return null; }
            catch (Exception ex) when (ex is IOException or HttpRequestException) { MarkFailure(key); return null; }
            finally { Downloads.Release(); }
            try
            {
                Directory.CreateDirectory(_directory);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false); File.Move(temporary, path, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                Trim(path, bytes.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return bytes;
        }
        finally { gate.Release(); }
    }
    private static bool AllowedHost(string host) => host is "cdn.cloudflare.steamstatic.com" or "cdn.akamai.steamstatic.com"
        or "shared.fastly.steamstatic.com" or "shared.akamai.steamstatic.com" or "steamcdn-a.akamaihd.net";
    private bool IsBackingOff(string key)
    {
        lock (_failureSync)
        {
            if (!_failures.TryGetValue(key, out var failedAt)) return false;
            if (DateTimeOffset.UtcNow - failedAt < TimeSpan.FromMinutes(10)) return true;
            _failures.Remove(key); return false;
        }
    }
    private void MarkFailure(string key)
    {
        lock (_failureSync)
        {
            if (_failures.Count >= 1000) _failures.Remove(_failures.Keys.First());
            _failures[key] = DateTimeOffset.UtcNow;
        }
    }
    private static async Task<byte[]?> ReadFileAsync(string path, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                32768, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaxBytes) return null;
            return await ReadBoundedAsync(stream, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[32768];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > MaxBytes) return null;
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private void Trim(string path, long length)
    {
        // Scan the app-owned directory once. Subsequent writes update the bounded index instead
        // of enumerating hundreds of files after every downloaded thumbnail.
        lock (_diskSync)
        {
            _diskEntries ??= new DirectoryInfo(_directory).GetFiles("*.img")
                .ToDictionary(file => file.FullName, file => (file.Length, file.LastWriteTimeUtc), StringComparer.OrdinalIgnoreCase);
            _diskEntries[Path.GetFullPath(path)] = (length, DateTime.UtcNow);
            var bytes = _diskEntries.Values.Sum(file => file.Length);
            if (_diskEntries.Count <= 300 && bytes <= 200L * 1024 * 1024) return;
            foreach (var file in _diskEntries.OrderBy(item => item.Value.Modified).ToArray())
            {
                if (_diskEntries.Count <= 300 && bytes <= 200L * 1024 * 1024) break;
                try
                {
                    File.Delete(file.Key);
                    _diskEntries.Remove(file.Key); bytes -= file.Value.Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
