using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheIntroDB.Configuration;

namespace TheIntroDB.Api;

/// <summary>
/// HTTP client for TheIntroDB API (GET /media).
/// </summary>
public class TheIntroDbClient
{
    private readonly HttpClient _httpClient;
    private readonly Plugin _plugin;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TheIntroDbClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client for requests.</param>
    /// <param name="plugin">Plugin instance for configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public TheIntroDbClient(HttpClient httpClient, Plugin plugin, ILogger logger)
    {
        _httpClient = httpClient;
        _plugin = plugin;
        _logger = logger;
    }

    /// <summary>
    /// Fetches media segment timestamps for the given TMDB id or IMDB id (movie) or episode.
    /// </summary>
    /// <param name="tmdbId">Optional TMDB ID of the movie or series.</param>
    /// <param name="imdbId">Optional IMDB ID of the movie or episode (tt[0-9]{7,8}). Used when no TMDB ID is available.</param>
    /// <param name="isMovie">True for movie, false for TV episode.</param>
    /// <param name="season">Season number (required for TV).</param>
    /// <param name="episode">Episode number (required for TV).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Media response or null if not found or error.</returns>
    public async Task<MediaResponse?> GetMediaAsync(
        int? tmdbId,
        string? imdbId,
        bool isMovie,
        int? season,
        int? episode,
        CancellationToken cancellationToken)
    {
        var config = _plugin.Configuration ?? new PluginConfiguration();
        const string baseUrl = "https://api.theintrodb.org/v2";

        var tmdbIdValue = tmdbId.GetValueOrDefault();
        var hasTmdb = tmdbIdValue > 0;
        var hasImdb = !string.IsNullOrWhiteSpace(imdbId);

        if (!hasTmdb && !hasImdb)
        {
            return null;
        }

        string query;
        if (hasTmdb)
        {
            query = isMovie
                ? $"?tmdb_id={tmdbIdValue}"
                : $"?tmdb_id={tmdbIdValue}&season={season}&episode={episode}";
        }
        else
        {
            var encodedImdb = Uri.EscapeDataString(imdbId!);
            query = isMovie
                ? $"?imdb_id={encodedImdb}"
                : $"?imdb_id={encodedImdb}&season={season}&episode={episode}";
        }

        var requestUri = new Uri(baseUrl + "/media" + query, UriKind.Absolute);
        _logger.LogInformation("TheIntroDB API request: {Uri}", requestUri);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        }

        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("TheIntroDB API response: StatusCode={StatusCode} for {Uri}", response.StatusCode, requestUri);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("TheIntroDB API error response body: {Body}", string.IsNullOrEmpty(body) ? "(empty)" : body.Length > 500 ? body[..500] + "..." : body);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = System.Text.Json.JsonSerializer.Deserialize<MediaResponse>(json);
            _logger.LogDebug(
                "TheIntroDB API parsed response: IntroCount={IntroCount}, RecapCount={RecapCount}, CreditsCount={CreditsCount}, PreviewCount={PreviewCount}",
                result?.Intro?.Count ?? 0,
                result?.Recap?.Count ?? 0,
                result?.Credits?.Count ?? 0,
                result?.Preview?.Count ?? 0);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TheIntroDB API request failed for {Uri}", requestUri);
            return null;
        }
    }
}
