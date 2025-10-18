using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain;
using Domain.Completion;

namespace Integration.CompletionTimes;

public sealed class HowLongToBeatCompletionTimeFetcher : ICompletionTimeFetcher
{
    private readonly HttpClient _httpClient;

    public HowLongToBeatCompletionTimeFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SteamBacklogPicker/1.0");
        }
    }

    public async Task<TimeSpan?> FetchEstimatedCompletionAsync(GameEntry game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(game.Title))
        {
            return null;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "https://howlongtobeat.com/api/search")
        {
            Content = JsonContent.Create(new
            {
                searchType = "games",
                searchTerms = game.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                searchPage = 1,
                size = 1,
                searchOptions = new
                {
                    games = new
                    {
                        userId = 0,
                        platform = "",
                        sortCategory = "popular",
                        rangeCategory = "main",
                        gameplay = new int[] { },
                        rangeTime = new int[] { },
                        gameplayCompletion = 0,
                    },
                    users = new { },
                },
            })
        };
        request.Headers.Referrer = new Uri("https://howlongtobeat.com");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in dataElement.EnumerateArray())
            {
                if (TryReadCompletion(item, out var estimate))
                {
                    return estimate;
                }
            }

            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static bool TryReadCompletion(JsonElement element, out TimeSpan? estimate)
    {
        estimate = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadDouble(element, "gameplayMain", out var mainHours) ||
            TryReadDouble(element, "comp_main", out mainHours) ||
            TryReadDouble(element, "gameplay_main", out mainHours))
        {
            if (mainHours > 0)
            {
                estimate = TimeSpan.FromHours(mainHours);
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.Number when property.TryGetDouble(out value):
                return true;
            case JsonValueKind.String when double.TryParse(property.GetString(), out value):
                return true;
            default:
                return false;
        }
    }
}
