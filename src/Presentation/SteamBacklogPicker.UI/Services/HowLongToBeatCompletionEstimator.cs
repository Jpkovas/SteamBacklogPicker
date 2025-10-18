using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SteamBacklogPicker.UI.Services;

public sealed class HowLongToBeatCompletionEstimator : IGameCompletionEstimator
{
    private static readonly Uri BaseUri = new("https://howlongtobeat.com/");
    private readonly HttpClient _httpClient;

    public HowLongToBeatCompletionEstimator(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { BaseAddress = BaseUri };
    }

    public async Task<TimeSpan?> GetEstimatedCompletionAsync(string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/search");
        request.Headers.UserAgent.ParseAdd("SteamBacklogPicker/1.0");
        request.Content = JsonContent.Create(new
        {
            searchType = "games",
            searchTerms = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            searchPage = 1,
            size = 1,
            searchOptions = new
            {
                games = new
                {
                    userId = 0,
                    platform = string.Empty,
                    sortCategory = "popular",
                    rangeCategory = "main",
                    rangeTime = new[] { 0, 0 },
                    gameplay = new { },
                    modifier = string.Empty
                },
                users = new { },
                filter = string.Empty,
                sort = 0
            }
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array || dataElement.GetArrayLength() == 0)
        {
            return null;
        }

        var entry = dataElement[0];
        if (TryReadMinutes(entry, "gameplayMain", out var mainMinutes) && mainMinutes > 0)
        {
            return TimeSpan.FromMinutes(mainMinutes);
        }

        if (TryReadMinutes(entry, "gameplayMainExtra", out var extraMinutes) && extraMinutes > 0)
        {
            return TimeSpan.FromMinutes(extraMinutes);
        }

        return null;
    }

    private static bool TryReadMinutes(JsonElement element, string propertyName, out double minutes)
    {
        minutes = 0;
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric))
        {
            minutes = numeric;
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                minutes = parsed;
                return true;
            }
        }

        return false;
    }
}
