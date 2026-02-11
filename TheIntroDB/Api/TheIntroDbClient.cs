using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TheIntroDB.Configuration;

namespace TheIntroDB.Api;

/// <summary>
/// HTTP client for TheIntroDB API (GET /media).
/// </summary>
public class TheIntroDbClient
{
    private readonly HttpClient _httpClient;
    private readonly Plugin _plugin;

    /// <summary>
    /// Initializes a new instance of the <see cref="TheIntroDbClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client for requests.</param>
    /// <param name="plugin">Plugin instance for configuration.</param>
    public TheIntroDbClient(HttpClient httpClient, Plugin plugin)
    {
        _httpClient = httpClient;
        _plugin = plugin;
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
        const string baseUrl = "https://api.theintrodb.org/v1";

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
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        }

        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<MediaResponse>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
