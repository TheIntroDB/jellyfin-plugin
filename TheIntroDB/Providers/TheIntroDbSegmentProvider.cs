using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheIntroDB.Api;
using TheIntroDB.Configuration;

namespace TheIntroDB.Providers;

/// <summary>
/// Media segment provider that fetches intro/recap/credits/preview from TheIntroDB API and returns Jellyfin media segments.
/// </summary>
public class TheIntroDbSegmentProvider : IMediaSegmentProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TheIntroDbSegmentProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TheIntroDbSegmentProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for API requests.</param>
    /// <param name="libraryManager">Library manager to resolve items.</param>
    /// <param name="serviceProvider">Service provider for lazy resolution of IMediaSegmentManager (avoids circular dependency).</param>
    /// <param name="logger">Logger instance.</param>
    public TheIntroDbSegmentProvider(
        IHttpClientFactory httpClientFactory,
        ILibraryManager libraryManager,
        IServiceProvider serviceProvider,
        ILogger<TheIntroDbSegmentProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _libraryManager = libraryManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _logger.LogInformation("TheIntroDB segment provider constructed");
    }

    /// <inheritdoc />
    public string Name => Plugin.Instance?.Name ?? "TheIntroDB";

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(
        MediaSegmentGenerationRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetMediaSegments called for ItemId={ItemId}", request?.ItemId);

        if (request is null || Plugin.Instance is null)
        {
            _logger.LogWarning("Early exit: request or Plugin.Instance is null");
            return Array.Empty<MediaSegmentDto>();
        }

        if (Plugin.Instance.Configuration is not PluginConfiguration config)
        {
            _logger.LogWarning("Early exit: Plugin configuration is not PluginConfiguration");
            return Array.Empty<MediaSegmentDto>();
        }

        var item = _libraryManager.GetItemById(request.ItemId);
        if (item is null)
        {
            _logger.LogWarning("Early exit: item not found for ItemId={ItemId}", request.ItemId);
            return Array.Empty<MediaSegmentDto>();
        }

        var selectedShowIds = GetSelectedShowIds(config);
        if (selectedShowIds.Count > 0)
        {
            if (item is not Episode selectedEpisode)
            {
                _logger.LogDebug("Skipping {Name}: selected show filter is set and item is not an episode", item.Name);
                return GetExistingSegments(request);
            }

            if (!IsEpisodeInSelectedShows(selectedEpisode, selectedShowIds))
            {
                _logger.LogDebug(
                    "Skipping {Name}: series does not match any selected show filter ({SelectedShowCount} selected)",
                    item.Name,
                    selectedShowIds.Count);
                return GetExistingSegments(request);
            }
        }

        int? tmdbId = null;
        int? tvdbId = null;
        string? imdbId = null;
        bool isMovie = false;
        int? season = null;
        int? episode = null;

        if (item is Movie movie)
        {
            isMovie = true;
            tmdbId = GetTmdbId(movie);
            tvdbId = GetTvdbId(movie);
            imdbId = GetImdbId(movie);
            _logger.LogInformation("Movie: Name={Name}, TmdbId={TmdbId}, TvdbId={TvdbId}, ImdbId={ImdbId}", item.Name, tmdbId, tvdbId, imdbId ?? "(none)");
        }
        else if (item is Episode ep)
        {
            tmdbId = GetTmdbId(ep.Series);
            tvdbId = GetTvdbId(ep.Series);
            imdbId = GetImdbId(ep) ?? GetImdbId(ep.Series);
            season = ep.ParentIndexNumber;
            episode = ep.IndexNumber;
            _logger.LogInformation("Episode: Name={Name}, Series={Series}, S{Season}E{Episode}, TmdbId={TmdbId}, TvdbId={TvdbId}, ImdbId={ImdbId}", item.Name, ep.SeriesName, season, episode, tmdbId, tvdbId, imdbId ?? "(none)");
        }

        if ((!tmdbId.HasValue || tmdbId.Value <= 0) && (!tvdbId.HasValue || tvdbId.Value <= 0) && string.IsNullOrWhiteSpace(imdbId))
        {
            _logger.LogWarning("Early exit: no TmdbId, TvdbId, or ImdbId for {Name}", item.Name);
            return Array.Empty<MediaSegmentDto>();
        }

        if (!isMovie && (!season.HasValue || !episode.HasValue))
        {
            _logger.LogWarning("Early exit: TV episode missing season/episode for {Name}", item.Name);
            return Array.Empty<MediaSegmentDto>();
        }

        if (config.IgnoreMediaWithExistingSegments)
        {
            var segmentManager = _serviceProvider.GetRequiredService<IMediaSegmentManager>();
            if (segmentManager.HasSegments(request.ItemId))
            {
                _logger.LogDebug("Skipping {Name}: already has segments (IgnoreMediaWithExistingSegments enabled)", item.Name);
                Plugin.AnonymousUsageReporter.TrackEvent(
                    Plugin.Instance,
                    "segments_generation_skipped_existing",
                    new Dictionary<string, object>
                    {
                        ["host"] = "jellyfin",
                        ["media_type"] = isMovie ? "movie" : "episode",
                        ["has_tmdb"] = tmdbId.HasValue && tmdbId.Value > 0 ? 1 : 0,
                        ["has_tvdb"] = tvdbId.HasValue && tvdbId.Value > 0 ? 1 : 0,
                        ["has_imdb"] = !string.IsNullOrWhiteSpace(imdbId) ? 1 : 0,
                        ["existing_segments_count"] = request.ExistingSegments?.Count ?? 0,
                        ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                    });

                // Return existing segments unchanged to prevent Jellyfin from deleting them.
                return GetExistingSegments(request);
            }
        }

        _logger.LogInformation("Fetching from TheIntroDB API: tmdbId={TmdbId}, tvdbId={TvdbId}, imdbId={ImdbId}, isMovie={IsMovie}, season={Season}, episode={Episode}", tmdbId, tvdbId, imdbId, isMovie, season, episode);
        var httpClient = _httpClientFactory.CreateClient();
        var client = new TheIntroDbClient(httpClient, Plugin.Instance, _logger);
        long? durationMs = item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0
            ? item.RunTimeTicks.Value / TimeSpan.TicksPerMillisecond
            : null;
        var media = await client.GetMediaAsync(tmdbId, tvdbId, imdbId, isMovie, season, episode, durationMs, cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            _logger.LogInformation("TheIntroDB API returned no data for {Name}", item.Name);
            return Array.Empty<MediaSegmentDto>();
        }

        long? runTimeTicks = item.RunTimeTicks;

        var segments = new List<MediaSegmentDto>();

        if (config.EnableIntro && AddSegment(media.Intro, true, MediaSegmentType.Intro, request.ItemId, runTimeTicks, segments))
        {
            // Added
        }

        if (config.EnableRecap && AddSegment(media.Recap, true, MediaSegmentType.Recap, request.ItemId, runTimeTicks, segments))
        {
            // Added
        }

        if (config.EnableCredits && AddSegment(media.Credits, false, MediaSegmentType.Outro, request.ItemId, runTimeTicks, segments))
        {
            // Added
        }

        if (config.EnablePreview && AddSegment(media.Preview, false, MediaSegmentType.Preview, request.ItemId, runTimeTicks, segments))
        {
            // Added
        }

        var introCount = segments.Count(s => s.Type == MediaSegmentType.Intro);
        var recapCount = segments.Count(s => s.Type == MediaSegmentType.Recap);
        var creditsCount = segments.Count(s => s.Type == MediaSegmentType.Outro);
        var previewCount = segments.Count(s => s.Type == MediaSegmentType.Preview);
        Plugin.AnonymousUsageReporter.TrackEvent(
            Plugin.Instance,
            "segments_generated",
            new Dictionary<string, object>
            {
                ["host"] = "jellyfin",
                ["media_type"] = isMovie ? "movie" : "episode",
                ["has_tmdb"] = tmdbId.HasValue && tmdbId.Value > 0 ? 1 : 0,
                ["has_tvdb"] = tvdbId.HasValue && tvdbId.Value > 0 ? 1 : 0,
                ["has_imdb"] = !string.IsNullOrWhiteSpace(imdbId) ? 1 : 0,
                ["segments_total"] = segments.Count,
                ["segments_intro"] = introCount,
                ["segments_recap"] = recapCount,
                ["segments_credits"] = creditsCount,
                ["segments_preview"] = previewCount,
                ["enable_intro"] = config.EnableIntro ? 1 : 0,
                ["enable_recap"] = config.EnableRecap ? 1 : 0,
                ["enable_credits"] = config.EnableCredits ? 1 : 0,
                ["enable_preview"] = config.EnablePreview ? 1 : 0,
                ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
            });

        _logger.LogInformation("Returning {Count} segments for {Name}", segments.Count, item.Name);
        return segments;
    }

    /// <inheritdoc />
    public ValueTask<bool> Supports(BaseItem item)
    {
        var supported = item is Episode or Movie;
        _logger.LogDebug("Supports({Name}, {Type}): {Supported}", item?.Name ?? "null", item?.GetType().Name ?? "null", supported);
        return ValueTask.FromResult(supported);
    }

    private static List<MediaSegmentDto> GetExistingSegments(MediaSegmentGenerationRequest request)
    {
        return (request.ExistingSegments ?? Array.Empty<MediaSegmentDto>()).ToList();
    }

    private static HashSet<string> GetSelectedShowIds(PluginConfiguration config)
    {
        var selectedShowIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(config.SelectedShowIds))
        {
            foreach (var selectedShowId in config.SelectedShowIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalizedSelectedShowId = NormalizeShowId(selectedShowId);
                if (!string.IsNullOrWhiteSpace(normalizedSelectedShowId))
                {
                    selectedShowIds.Add(normalizedSelectedShowId);
                }
            }
        }

        var legacySelectedShowId = NormalizeShowId(config.SelectedShowId);
        if (!string.IsNullOrWhiteSpace(legacySelectedShowId))
        {
            selectedShowIds.Add(legacySelectedShowId);
        }

        return selectedShowIds;
    }

    private static bool IsEpisodeInSelectedShows(Episode episode, HashSet<string> selectedShowIds)
    {
        var seriesId = episode.Series?.Id;
        if (!seriesId.HasValue || seriesId.Value == Guid.Empty)
        {
            return false;
        }

        return selectedShowIds.Contains(seriesId.Value.ToString("D"))
            || selectedShowIds.Contains(seriesId.Value.ToString("N"));
    }

    private static string NormalizeShowId(string? selectedShowId)
    {
        if (string.IsNullOrWhiteSpace(selectedShowId))
        {
            return string.Empty;
        }

        return selectedShowId.Trim();
    }

    private static int? GetTmdbId(BaseItem item)
    {
        if (item?.ProviderIds is null)
        {
            return null;
        }

        if (item.ProviderIds.TryGetValue("Tmdb", out var id) && !string.IsNullOrWhiteSpace(id))
        {
            return int.TryParse(id, out var n) ? n : null;
        }

        return null;
    }

    private static string? GetImdbId(BaseItem item)
    {
        if (item?.ProviderIds is null)
        {
            return null;
        }

        if (item.ProviderIds.TryGetValue("Imdb", out var id) && !string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return null;
    }

    private static int? GetTvdbId(BaseItem item)
    {
        if (item?.ProviderIds is null)
        {
            return null;
        }

        if (item.ProviderIds.TryGetValue("Tvdb", out var id) && !string.IsNullOrWhiteSpace(id))
        {
            return int.TryParse(id, out var n) ? n : null;
        }

        return null;
    }

    private static bool AddSegment(
        IEnumerable<SegmentTimestamp>? stamps,
        bool endRequired,
        MediaSegmentType type,
        Guid itemId,
        long? runTimeTicks,
        List<MediaSegmentDto> segments)
    {
        if (stamps is null)
        {
            return false;
        }

        var added = false;
        foreach (var stamp in stamps)
        {
            if (stamp is null || !stamp.HasValidRange(endRequired))
            {
                continue;
            }

            long startMs = stamp.StartMs ?? 0;
            long endMs;

            if (stamp.EndMs.HasValue && stamp.EndMs.Value > 0)
            {
                endMs = stamp.EndMs.Value;
            }
            else if (runTimeTicks.HasValue && runTimeTicks.Value > 0)
            {
                endMs = runTimeTicks.Value / TimeSpan.TicksPerMillisecond;
            }
            else
            {
                continue;
            }

            if (endMs <= startMs)
            {
                continue;
            }

            long startTicks = startMs * TimeSpan.TicksPerMillisecond;
            long endTicks = endMs * TimeSpan.TicksPerMillisecond;

            segments.Add(new MediaSegmentDto
            {
                StartTicks = startTicks,
                EndTicks = endTicks,
                ItemId = itemId,
                Type = type
            });
            added = true;
        }

        return added;
    }
}
