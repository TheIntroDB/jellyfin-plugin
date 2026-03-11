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
/// Rate limit: ~30 requests per 10 seconds (per IP). We throttle to stay under this.
/// </summary>
public class TheIntroDbClient
{
    private const int MaxRequestsPerWindow = 30;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinDelayBetweenRequests = TimeSpan.FromMilliseconds(RateLimitWindow.TotalMilliseconds / MaxRequestsPerWindow);

    private static readonly SemaphoreSlim RateLimitLock = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;

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
        if (DateTime.UtcNow < Plugin.RateLimitExpiryUtc)
        {
            _logger.LogWarning(
                "TheIntroDB API rate limit is currently active. Skipping request. The rate limit will reset at {RateLimitExpiryUtc} UTC.",
                Plugin.RateLimitExpiryUtc);
            return null;
        }

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
            await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("TheIntroDB API response: StatusCode={StatusCode} for {Uri}", response.StatusCode, requestUri);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfterSeconds = GetRetryAfterSeconds(response.Headers);
                Plugin.RateLimitExpiryUtc = DateTime.UtcNow.AddSeconds(retryAfterSeconds);
                _logger.LogWarning(
                    "TheIntroDB API rate limit exceeded. Will not send requests until {RateLimitExpiryUtc} UTC. Retry-after: {RetryAfterSeconds}s",
                    Plugin.RateLimitExpiryUtc,
                    retryAfterSeconds);

                return null;
            }

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

    private static int GetRetryAfterSeconds(HttpResponseHeaders headers)
    {
        if (headers.TryGetValues("X-UsageLimit-Reset", out var usageResetValues) && int.TryParse(usageResetValues.FirstOrDefault(), out var usageResetSeconds))
        {
            return usageResetSeconds;
        }

        if (headers.TryGetValues("X-RateLimit-Reset", out var rateResetValues) && int.TryParse(rateResetValues.FirstOrDefault(), out var rateResetSeconds))
        {
            return rateResetSeconds;
        }

        if (headers.RetryAfter?.Delta.HasValue ?? false)
        {
            return (int)headers.RetryAfter.Delta.Value.TotalSeconds;
        }

        // Default to a 5-minute wait if no header is present
        return 300;
    }


    /// <summary>
    /// Waits if necessary to respect the API rate limit (30 requests per 10 seconds).
    /// </summary>
    private static async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        await RateLimitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRequestUtc;
            if (elapsed < MinDelayBetweenRequests)
            {
                var waitTime = MinDelayBetweenRequests - elapsed;
                await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            RateLimitLock.Release();
        }
    }
}
