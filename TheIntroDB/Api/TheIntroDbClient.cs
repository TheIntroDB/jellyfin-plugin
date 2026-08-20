using System;
using System.Collections.Generic;
using System.Linq;
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
    private const int MaxRequestsPerWindow = 25;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinDelayBetweenRequests = TimeSpan.FromMilliseconds(RateLimitWindow.TotalMilliseconds / MaxRequestsPerWindow);
    private static readonly TimeSpan MaxRateLimitDelay = TimeSpan.FromMinutes(5);

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
    /// Fetches media segment timestamps for the given TMDB / TVDB / IMDB id (movie) or episode.
    /// </summary>
    /// <param name="tmdbId">Optional TMDB ID of the movie or series.</param>
    /// <param name="tvdbId">Optional TVDB ID of the movie or series. Used when no TMDB ID is available.</param>
    /// <param name="imdbId">Optional IMDB ID of the movie or episode (tt[0-9]{7,8}). Used when no TMDB ID is available.</param>
    /// <param name="isMovie">True for movie, false for TV episode.</param>
    /// <param name="season">Season number (required for TV).</param>
    /// <param name="episode">Episode number (required for TV).</param>
    /// <param name="durationMs">Optional total video duration (milliseconds). Recommended for best matching release version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Media fetch result distinguishing rate limits, errors and not-found.</returns>
    public async Task<MediaFetchResult> GetMediaAsync(
        int? tmdbId,
        int? tvdbId,
        string? imdbId,
        bool isMovie,
        int? season,
        int? episode,
        long? durationMs,
        CancellationToken cancellationToken)
    {
        var tmdbIdValue = tmdbId.GetValueOrDefault();
        var hasTmdb = tmdbIdValue > 0;
        var tvdbIdValue = tvdbId.GetValueOrDefault();
        var hasTvdb = tvdbIdValue > 0;
        var hasImdb = !string.IsNullOrWhiteSpace(imdbId);
        var idSource = hasTmdb ? "tmdb" : hasTvdb ? "tvdb" : hasImdb ? "imdb" : "none";

        if (DateTime.UtcNow < Plugin.RateLimitExpiryUtc)
        {
            _logger.LogWarning(
                "TheIntroDB API rate limit is currently active. Skipping request. The rate limit will reset at {RateLimitExpiryUtc} UTC.",
                Plugin.RateLimitExpiryUtc);
            Plugin.AnonymousUsageReporter.TrackEvent(
                _plugin,
                "theintrodb_api_media_fetch",
                new Dictionary<string, object>
                {
                    ["host"] = "jellyfin",
                    ["result"] = "local_ratelimit_active",
                    ["media_type"] = isMovie ? "movie" : "episode",
                    ["id_source"] = idSource,
                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(_plugin.Configuration?.ApiKey) ? 1 : 0
                });
            return MediaFetchResult.RateLimited();
        }

        var config = _plugin.Configuration ?? new PluginConfiguration();
        const string baseUrl = "https://api.theintrodb.org/v3";

        if (!hasTmdb && !hasTvdb && !hasImdb)
        {
            return MediaFetchResult.NotFound();
        }

        var queryParams = new List<string>(4);
        if (hasTmdb)
        {
            queryParams.Add($"tmdb_id={tmdbIdValue}");
        }
        else if (hasTvdb)
        {
            queryParams.Add($"tvdb_id={tvdbIdValue}");
        }
        else
        {
            queryParams.Add($"imdb_id={Uri.EscapeDataString(imdbId!)}");
        }

        if (!isMovie)
        {
            if (!season.HasValue || !episode.HasValue)
            {
                _logger.LogWarning("Skipping TV show request: missing season ({Season}) or episode ({Episode}) for tmdbId={TmdbId}, tvdbId={TvdbId}, imdbId={ImdbId}", season, episode, tmdbIdValue, tvdbIdValue, imdbId ?? "(none)");
                return MediaFetchResult.NotFound();
            }

            queryParams.Add($"season={season}");
            queryParams.Add($"episode={episode}");
        }

        if (durationMs.HasValue && durationMs.Value > 0)
        {
            queryParams.Add($"duration_ms={durationMs.Value}");
        }

        var query = "?" + string.Join("&", queryParams);

        var requestUri = new Uri(baseUrl + "/media" + query, UriKind.Absolute);
        _logger.LogInformation("TheIntroDB API request: {Uri}", requestUri);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        }

        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        var version = _plugin.GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("theintrodb-jellyfin-plugin", version));

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

                Plugin.AnonymousUsageReporter.TrackEvent(
                    _plugin,
                    "theintrodb_api_media_fetch",
                    new Dictionary<string, object>
                    {
                        ["host"] = "jellyfin",
                        ["result"] = "http_429",
                        ["media_type"] = isMovie ? "movie" : "episode",
                        ["id_source"] = idSource,
                        ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                    });
                return MediaFetchResult.RateLimited();
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("TheIntroDB API error response body: {Body}", string.IsNullOrEmpty(body) ? "(empty)" : body.Length > 500 ? body[..500] + "..." : body);
                Plugin.AnonymousUsageReporter.TrackEvent(
                    _plugin,
                    "theintrodb_api_media_fetch",
                    new Dictionary<string, object>
                    {
                        ["host"] = "jellyfin",
                        ["result"] = "http_error",
                        ["status"] = (int)response.StatusCode,
                        ["media_type"] = isMovie ? "movie" : "episode",
                        ["id_source"] = idSource,
                        ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                    });

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return MediaFetchResult.NotFound();
                }

                return MediaFetchResult.Error();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = System.Text.Json.JsonSerializer.Deserialize<MediaResponse>(json);
            _logger.LogDebug(
                "TheIntroDB API parsed response: IntroCount={IntroCount}, RecapCount={RecapCount}, CreditsCount={CreditsCount}, PreviewCount={PreviewCount}",
                result?.Intro?.Count ?? 0,
                result?.Recap?.Count ?? 0,
                result?.Credits?.Count ?? 0,
                result?.Preview?.Count ?? 0);
            Plugin.AnonymousUsageReporter.TrackEvent(
                _plugin,
                "theintrodb_api_media_fetch",
                new Dictionary<string, object>
                {
                    ["host"] = "jellyfin",
                    ["result"] = result is null ? "success_null" : "success",
                    ["media_type"] = isMovie ? "movie" : "episode",
                    ["id_source"] = idSource,
                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0,
                    ["intro_count"] = result?.Intro?.Count ?? 0,
                    ["recap_count"] = result?.Recap?.Count ?? 0,
                    ["credits_count"] = result?.Credits?.Count ?? 0,
                    ["preview_count"] = result?.Preview?.Count ?? 0
                });
            return MediaFetchResult.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is cooperative — propagate it rather than masking
            // it as a transient API error.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TheIntroDB API request failed for {Uri}", requestUri);
            Plugin.AnonymousUsageReporter.TrackEvent(
                _plugin,
                "theintrodb_api_media_fetch",
                new Dictionary<string, object>
                {
                    ["host"] = "jellyfin",
                    ["result"] = "exception",
                    ["media_type"] = isMovie ? "movie" : "episode",
                    ["id_source"] = idSource,
                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                });
            return MediaFetchResult.Error();
        }
    }

    private static int GetRetryAfterSeconds(HttpResponseHeaders headers)
    {
        if (headers.TryGetValues("X-UsageLimit-Reset", out var usageResetValues) && int.TryParse(usageResetValues.FirstOrDefault(), out var usageResetSeconds))
        {
            return ClampRetryAfterSeconds(usageResetSeconds);
        }

        if (headers.TryGetValues("X-RateLimit-Reset", out var rateResetValues) && int.TryParse(rateResetValues.FirstOrDefault(), out var rateResetSeconds))
        {
            return ClampRetryAfterSeconds(rateResetSeconds);
        }

        if (headers.RetryAfter?.Delta.HasValue ?? false)
        {
            return ClampRetryAfterSeconds((int)headers.RetryAfter.Delta.Value.TotalSeconds);
        }

        if (headers.RetryAfter?.Date.HasValue ?? false)
        {
            return ClampRetryAfterSeconds((int)Math.Ceiling((headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow).TotalSeconds));
        }

        // Default to a 5-minute wait if no header is present
        return (int)MaxRateLimitDelay.TotalSeconds;
    }

    private static int ClampRetryAfterSeconds(int seconds)
    {
        return Math.Max(1, Math.Min(seconds, (int)MaxRateLimitDelay.TotalSeconds));
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
