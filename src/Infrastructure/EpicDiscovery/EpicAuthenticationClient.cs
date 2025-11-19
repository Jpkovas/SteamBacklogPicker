using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SteamClientAdapter;

namespace EpicDiscovery;

public sealed class EpicAuthenticationClient
{
    private const string TokenEndpoint = "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token";
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(4);

    private readonly HttpClient httpClient;
    private readonly IFileAccessor fileAccessor;
    private readonly ILogger<EpicAuthenticationClient>? logger;
    private readonly object syncRoot = new();

    private string? cachedToken;
    private DateTimeOffset cachedExpiration;

    public EpicAuthenticationClient(HttpClient httpClient, IFileAccessor fileAccessor, ILogger<EpicAuthenticationClient>? logger = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.fileAccessor = fileAccessor ?? throw new ArgumentNullException(nameof(fileAccessor));
        this.logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            if (!string.IsNullOrWhiteSpace(cachedToken) && cachedExpiration > DateTimeOffset.UtcNow.AddMinutes(-5))
            {
                return cachedToken;
            }
        }

        var refreshToken = TryReadLatestToken();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            logger?.LogWarning("No Epic launcher refresh token could be found in LocalAppData caches.");
            return null;
        }

        try
        {
            var token = await RequestAccessTokenAsync(refreshToken!, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token))
            {
                lock (syncRoot)
                {
                    cachedToken = token;
                    cachedExpiration = DateTimeOffset.UtcNow.Add(AccessTokenLifetime);
                }
            }

            return token;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to exchange Epic launcher refresh token for an access token.");
            return null;
        }
    }

    private async Task<string?> RequestAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["token_type"] = "eg1",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = content,
        };

        // Standard launcher client ID; intentionally keeps the secret empty to mirror the native launcher.
        var launcherClient = Convert.ToBase64String(Encoding.UTF8.GetBytes("ec684b8c687f479fadea3cb2ad83f5c6:"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", launcherClient);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger?.LogWarning("Epic OAuth token exchange failed with status {StatusCode}", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("access_token", out var tokenElement) && tokenElement.ValueKind == JsonValueKind.String)
        {
            return tokenElement.GetString();
        }

        return null;
    }

    private string? TryReadLatestToken()
    {
        try
        {
            foreach (var file in EnumerateLauncherTokenFiles())
            {
                try
                {
                    var content = fileAccessor.ReadAllText(file);
                    var candidate = ExtractToken(content);
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Unable to parse token candidate file {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to inspect Epic launcher token caches");
        }

        return null;
    }

    private static IEnumerable<string> EnumerateLauncherTokenFiles()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            yield break;
        }

        var configRoot = Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Config", "Windows");
        if (Directory.Exists(configRoot))
        {
            foreach (var path in Directory.EnumerateFiles(configRoot, "*.*", SearchOption.AllDirectories))
            {
                yield return path;
            }
        }

        var webCacheRoot = Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "webcache");
        if (Directory.Exists(webCacheRoot))
        {
            foreach (var path in Directory.EnumerateFiles(webCacheRoot, "*.json", SearchOption.AllDirectories))
            {
                yield return path;
            }

            foreach (var path in Directory.EnumerateFiles(webCacheRoot, "*.log", SearchOption.AllDirectories))
            {
                yield return path;
            }
        }
    }

    private static string? ExtractToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            if (text.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("refresh_token", out var refresh) && refresh.ValueKind == JsonValueKind.String)
                {
                    return refresh.GetString();
                }

                if (document.RootElement.TryGetProperty("refreshToken", out var camelRefresh) && camelRefresh.ValueKind == JsonValueKind.String)
                {
                    return camelRefresh.GetString();
                }
            }
        }
        catch
        {
            // fall back to textual parsing
        }

        var marker = "eg1~";
        var index = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var span = text.AsSpan(index);
        var end = span.IndexOfAny('"', '\'', '\n', '\r', ' ');
        if (end > 0)
        {
            span = span[..end];
        }

        var token = span.ToString();
        return token.StartsWith(marker, true, CultureInfo.InvariantCulture) ? token : null;
    }
}
