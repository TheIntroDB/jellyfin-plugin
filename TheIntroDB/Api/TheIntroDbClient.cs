using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheIntroDB.Configuration;

namespace TheIntroDB.Api;

/// <summary>
/// HTTP client for TheIntroDB API (GET /media).
/// Rate limit: ~30 requests per 10 seconds (per user/IP), plus a daily usage
/// budget (per user/IP). We throttle to stay under the window, and when a 429
/// shows the daily budget is exhausted we park until the bucket resets instead
/// of probing every few minutes.
/// </summary>
public class TheIntroDbClient
{
    private const int MaxRequestsPerWindow = 25;
    private const int RateResetClampSeconds = 5 * 60;
    private const int UsageResetClampSeconds = 24 * 60 * 60;
    private const int DefaultRetryAfterSeconds = 5 * 60;
    private const int MaxConsecutiveRateLimitMultiplier = 8;
    private const int UsageBudgetWarningThreshold = 50;

    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinDelayBetweenRequests = TimeSpan.FromMilliseconds(RateLimitWindow.TotalMilliseconds / MaxRequestsPerWindow);

    private static readonly SemaphoreSlim RateLimitLock = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;
    private static DateTime _nextAllowedSendUtc = DateTime.MinValue;
    private static int _consecutiveRateLimits;
    private static DateTime _lastSkipLoggedExpiryUtc = DateTime.MinValue;

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
            var expiryUtc = Plugin.RateLimitExpiryUtc;
            if (expiryUtc != _lastSkipLoggedExpiryUtc)
            {
                // One warning per back-off period instead of one per skipped item.
                _lastSkipLoggedExpiryUtc = expiryUtc;
                _logger.LogWarning(
                    "TheIntroDB API rate limit is currently active. Skipping request. The rate limit will reset at {RateLimitExpiryUtc} UTC.",
                    expiryUtc);
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
            }
            else
            {
                _logger.LogDebug(
                    "TheIntroDB API rate limit is currently active until {RateLimitExpiryUtc} UTC.",
                    expiryUtc);
            }

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
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var retryAfterSeconds = GetRetryAfterSeconds(response.Headers, body);
                var consecutive = Interlocked.Increment(ref _consecutiveRateLimits);
                var isUsageLimit = IsUsageLimitResponse(body) || retryAfterSeconds > RateResetClampSeconds;
                var waitSeconds = isUsageLimit
                    ? retryAfterSeconds
                    : ApplyConsecutiveBackOff(retryAfterSeconds, consecutive);

                Plugin.RateLimitExpiryUtc = DateTime.UtcNow.AddSeconds(waitSeconds);
                _logger.LogWarning(
                    "TheIntroDB API {LimitKind} exceeded. Will not send requests until {RateLimitExpiryUtc} UTC. Retry-after: {RetryAfterSeconds}s. Consecutive 429 responses: {Consecutive429Count}",
                    isUsageLimit ? "daily usage limit" : "rate limit",
                    Plugin.RateLimitExpiryUtc,
                    retryAfterSeconds,
                    consecutive);

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

            Interlocked.Exchange(ref _consecutiveRateLimits, 0);
            UpdateRateWindowFromHeaders(response.Headers);

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
            LogUsageBudgetIfLow(response.Headers);
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

    /// <summary>
    /// Computes how long to wait after a 429. Usage-limit responses carry the
    /// daily bucket's reset (seconds until UTC midnight) and must be trusted
    /// for up to 24 hours — clamping them to five minutes turns an exhausted
    /// daily budget into a probe-every-five-minutes loop. Rate-limit responses
    /// carry a 10-second window and stay clamped to the five-minute ceiling.
    /// </summary>
    /// <param name="headers">Response headers.</param>
    /// <param name="body">Response body, used to detect usage-limit responses via their code.</param>
    /// <returns>Seconds to wait before the next request.</returns>
    internal static int GetRetryAfterSeconds(HttpResponseHeaders headers, string? body)
    {
        var isUsageLimit = IsUsageLimitResponse(body);
        var maxClamp = isUsageLimit ? UsageResetClampSeconds : RateResetClampSeconds;

        if (headers.TryGetValues("X-UsageLimit-Reset", out var usageResetValues)
            && int.TryParse(usageResetValues.FirstOrDefault(), out var usageResetSeconds)
            && usageResetSeconds > 0)
        {
            return ClampRetryAfterSeconds(usageResetSeconds, maxClamp);
        }

        if (headers.TryGetValues("X-RateLimit-Reset", out var rateResetValues)
            && int.TryParse(rateResetValues.FirstOrDefault(), out var rateResetSeconds)
            && rateResetSeconds > 0)
        {
            return ClampRetryAfterSeconds(rateResetSeconds, maxClamp);
        }

        if (headers.RetryAfter?.Delta.HasValue ?? false)
        {
            return ClampRetryAfterSeconds((int)headers.RetryAfter.Delta.Value.TotalSeconds, maxClamp);
        }

        if (headers.RetryAfter?.Date.HasValue ?? false)
        {
            return ClampRetryAfterSeconds((int)Math.Ceiling((headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow).TotalSeconds), maxClamp);
        }

        // Default to a 5-minute wait if no header is present
        return DefaultRetryAfterSeconds;
    }

    /// <summary>
    /// Grows the back-off wait across consecutive 429 responses so a still-exhausted
    /// bucket is probed less and less often instead of at a constant rate.
    /// </summary>
    /// <param name="baseSeconds">Base wait from the response headers.</param>
    /// <param name="consecutive">Number of consecutive 429 responses.</param>
    /// <returns>Adjusted wait in seconds, capped at the rate-limit ceiling.</returns>
    internal static int ApplyConsecutiveBackOff(int baseSeconds, int consecutive)
    {
        if (consecutive <= 1)
        {
            return baseSeconds;
        }

        var multiplier = Math.Min(consecutive, MaxConsecutiveRateLimitMultiplier);
        return Math.Min(baseSeconds * multiplier, RateResetClampSeconds);
    }

    private static int ClampRetryAfterSeconds(int seconds, int maxClamp)
    {
        return Math.Max(1, Math.Min(seconds, maxClamp));
    }

    private static bool IsUsageLimitResponse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.String
                && codeElement.GetString() is string code)
            {
                return code is "usage_limit_exceeded" or "specific_media_usage_limit_exceeded";
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. a proxy's plain-text 429): fall back to header parsing.
        }

        return false;
    }

    /// <summary>
    /// When the API advertises that almost no rate-limit budget remains, hold the
    /// next request until the window resets. Insurance against external consumers
    /// of the same bucket (other integrations sharing the key or public IP).
    /// </summary>
    /// <param name="headers">Response headers.</param>
    private static void UpdateRateWindowFromHeaders(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
            || !int.TryParse(remainingValues.FirstOrDefault(), out var remaining)
            || remaining > 1)
        {
            return;
        }

        if (!headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            || !int.TryParse(resetValues.FirstOrDefault(), out var resetSeconds)
            || resetSeconds <= 0)
        {
            return;
        }

        var resetUtc = DateTime.UtcNow.AddSeconds(ClampRetryAfterSeconds(resetSeconds, RateResetClampSeconds));
        if (resetUtc > _nextAllowedSendUtc)
        {
            _nextAllowedSendUtc = resetUtc;
        }
    }

    private void LogUsageBudgetIfLow(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("X-UsageLimit-Remaining", out var remainingValues)
            || !int.TryParse(remainingValues.FirstOrDefault(), out var remaining)
            || remaining >= UsageBudgetWarningThreshold)
        {
            return;
        }

        var limit = headers.TryGetValues("X-UsageLimit-Limit", out var limitValues)
            && int.TryParse(limitValues.FirstOrDefault(), out var parsedLimit)
            ? parsedLimit
            : (int?)null;

        _logger.LogDebug(
            "TheIntroDB daily usage budget nearly exhausted: {UsageRemaining} of {UsageLimit} requests remaining today.",
            remaining,
            limit?.ToString(CultureInfo.InvariantCulture) ?? "unknown");
    }

    /// <summary>
    /// Waits if necessary to respect the API rate limit (30 requests per 10 seconds),
    /// plus any hold imposed by a nearly-exhausted rate window.
    /// </summary>
    private static async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        await RateLimitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var waitUntil = now;
            if (_lastRequestUtc != DateTime.MinValue)
            {
                var pacedSend = _lastRequestUtc + MinDelayBetweenRequests;
                if (pacedSend > waitUntil)
                {
                    waitUntil = pacedSend;
                }
            }

            if (_nextAllowedSendUtc > waitUntil)
            {
                waitUntil = _nextAllowedSendUtc;
            }

            var waitTime = waitUntil - now;
            if (waitTime > TimeSpan.Zero)
            {
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
