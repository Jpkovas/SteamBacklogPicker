using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamClientAdapter;

namespace EpicDiscovery;

public sealed class EpicHeroArtCache
{
    private readonly HttpClient httpClient;
    private readonly IFileAccessor fileAccessor;
    private readonly ILogger<EpicHeroArtCache>? logger;
    private readonly string cacheDirectory;

    public EpicHeroArtCache(
        HttpClient httpClient,
        IFileAccessor fileAccessor,
        ILogger<EpicHeroArtCache>? logger = null,
        string? cacheDirectory = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.fileAccessor = fileAccessor ?? throw new ArgumentNullException(nameof(fileAccessor));
        this.logger = logger;
        this.cacheDirectory = cacheDirectory ?? BuildDefaultCacheDirectory();
    }

    public async Task<IReadOnlyCollection<EpicKeyImage>> PopulateAsync(
        IReadOnlyCollection<EpicKeyImage>? images,
        CancellationToken cancellationToken = default)
    {
        if (images is null || images.Count == 0)
        {
            return images ?? Array.Empty<EpicKeyImage>();
        }

        var updated = new List<EpicKeyImage>(images.Count);
        var changed = false;

        foreach (var image in images)
        {
            var cached = await EnsureCachedAsync(image, cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(image, cached))
            {
                changed = true;
            }

            updated.Add(cached);
        }

        return changed ? updated : images;
    }

    public IReadOnlyCollection<EpicKeyImage> AttachCachedPaths(IReadOnlyCollection<EpicKeyImage>? images)
    {
        if (images is null || images.Count == 0)
        {
            return images ?? Array.Empty<EpicKeyImage>();
        }

        var updated = new List<EpicKeyImage>(images.Count);
        var changed = false;

        foreach (var image in images)
        {
            if (!string.IsNullOrWhiteSpace(image.Path) && fileAccessor.FileExists(image.Path))
            {
                updated.Add(image);
                continue;
            }

            var cachedPath = TryGetCachedPath(image.Uri);
            if (cachedPath is not null)
            {
                updated.Add(image with { Path = cachedPath });
                changed = true;
            }
            else
            {
                updated.Add(image);
            }
        }

        return changed ? updated : images;
    }

    public string? TryGetCachedPath(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        var path = BuildCachePath(uri);
        return fileAccessor.FileExists(path) ? path : null;
    }

    private async Task<EpicKeyImage> EnsureCachedAsync(EpicKeyImage image, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(image.Path) && fileAccessor.FileExists(image.Path))
        {
            return image;
        }

        if (string.IsNullOrWhiteSpace(image.Uri))
        {
            return image;
        }

        var cachedPath = TryGetCachedPath(image.Uri);
        if (cachedPath is null)
        {
            cachedPath = await DownloadAsync(image.Uri, cancellationToken).ConfigureAwait(false);
        }

        return string.IsNullOrWhiteSpace(cachedPath) ? image : image with { Path = cachedPath };
    }

    private async Task<string?> DownloadAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogDebug("Hero art download for {Uri} failed with status {Status}", uri, response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var destination = BuildCachePath(uri);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                fileAccessor.CreateDirectory(directory);
            }
            fileAccessor.WriteAllBytes(destination, bytes);
            return destination;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            logger?.LogWarning(ex, "Failed to cache Epic hero art from {Uri}", uri);
            return null;
        }
    }

    private string BuildCachePath(string uri)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri))).ToLowerInvariant();
        var extension = DetermineExtension(uri);
        return Path.Combine(cacheDirectory, hash + extension);
    }

    private static string DetermineExtension(string uri)
    {
        try
        {
            var parsed = new Uri(uri, UriKind.Absolute);
            var ext = Path.GetExtension(parsed.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 8)
            {
                return ext;
            }
        }
        catch (UriFormatException)
        {
            // ignore malformed URIs; fall back to default extension
        }

        return ".img";
    }

    private static string BuildDefaultCacheDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "SteamBacklogPicker", "Epic", "Artwork");
    }
}
