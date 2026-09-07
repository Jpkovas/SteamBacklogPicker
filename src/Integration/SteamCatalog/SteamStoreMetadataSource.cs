using System.Net;
using System.Text.Json;

namespace SteamCatalog;

/// <summary>Public metadata only. This client never imports Steam cookies or account credentials.</summary>
public sealed class SteamStoreMetadataSource : ICatalogMetadataSource, IDisposable
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public SteamStoreMetadataSource(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _client = httpClient ?? new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<MetadataResponse> FetchAsync(uint appId, string language, CancellationToken cancellationToken)
    {
        if (appId == 0) throw new ArgumentOutOfRangeException(nameof(appId));
        var normalized = CatalogLanguage.Normalize(language);
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l={Uri.EscapeDataString(normalized)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("SteamBacklogPicker/1.0");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
                    retryAfter = date - DateTimeOffset.UtcNow;
                return new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, RetryAfter: retryAfter, ErrorCode: "store_throttled_or_unavailable");
            }
            if (!response.IsSuccessStatusCode)
                return new MetadataResponse(CatalogLookupStatus.Unavailable, ErrorCode: "store_http_error");
            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                return new MetadataResponse(CatalogLookupStatus.Unavailable, ErrorCode: "store_response_too_large");

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > MaxResponseBytes)
                    return new MetadataResponse(CatalogLookupStatus.Unavailable, ErrorCode: "store_response_too_large");
                buffer.Write(chunk, 0, read);
            }
            return ParseResponse(appId, normalized, buffer.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, ErrorCode: "store_timeout"); }
        catch (HttpRequestException)
        { return new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, ErrorCode: "store_network_error"); }
        catch (IOException)
        { return new MetadataResponse(CatalogLookupStatus.Unavailable, Retryable: true, ErrorCode: "store_read_error"); }
    }

    public static MetadataResponse ParseResponse(uint appId, string language, ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(appId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var app)
                || app.ValueKind != JsonValueKind.Object
                || !app.TryGetProperty("success", out var success))
                return Invalid();
            if (success.ValueKind == JsonValueKind.False)
                return new MetadataResponse(CatalogLookupStatus.NotFound);
            if (success.ValueKind != JsonValueKind.True || !app.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return Invalid();
            var name = ReadString(data, "name");
            if (string.IsNullOrWhiteSpace(name)) return Invalid();
            if (data.TryGetProperty("steam_appid", out var id) && (!id.TryGetUInt32(out var returnedId) || returnedId != appId)) return Invalid();
            var platforms = CatalogPlatforms.Unknown;
            if (data.TryGetProperty("platforms", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                if (IsTrue(os, "windows")) platforms |= CatalogPlatforms.Windows;
                if (IsTrue(os, "linux")) platforms |= CatalogPlatforms.Linux;
                if (IsTrue(os, "mac")) platforms |= CatalogPlatforms.MacOS;
            }
            return new MetadataResponse(CatalogLookupStatus.Found, new CatalogItem
            {
                AppId = appId,
                Language = CatalogLanguage.Normalize(language),
                Name = name,
                ProductType = ReadString(data, "type") ?? "unknown",
                Platforms = platforms,
                HeaderImageUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
                Source = "steam-store"
            });
        }
        catch (JsonException) { return Invalid(); }
        catch (InvalidOperationException) { return Invalid(); }
    }

    private static MetadataResponse Invalid() => new(CatalogLookupStatus.Unavailable, ErrorCode: "store_invalid_response");
    private static bool IsTrue(JsonElement value, string name)
        => value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.True;
    private static string? ReadString(JsonElement value, string name)
        => value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString()?.Trim() : null;

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
