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

        int? tmdbId = null;
        string? imdbId = null;
        bool isMovie = false;
        int? season = null;
        int? episode = null;

        if (item is Movie movie)
        {
            isMovie = true;
            tmdbId = GetTmdbId(movie);
            imdbId = GetImdbId(movie);
            _logger.LogInformation("Movie: Name={Name}, TmdbId={TmdbId}, ImdbId={ImdbId}", item.Name, tmdbId, imdbId ?? "(none)");
        }
        else if (item is Episode ep)
        {
            tmdbId = GetTmdbId(ep.Series);
            imdbId = GetImdbId(ep) ?? GetImdbId(ep.Series);
            season = ep.ParentIndexNumber;
            episode = ep.IndexNumber;
            _logger.LogInformation("Episode: Name={Name}, Series={Series}, S{Season}E{Episode}, TmdbId={TmdbId}, ImdbId={ImdbId}", item.Name, ep.SeriesName, season, episode, tmdbId, imdbId ?? "(none)");
        }

        if ((!tmdbId.HasValue || tmdbId.Value <= 0) && string.IsNullOrWhiteSpace(imdbId))
        {
            _logger.LogWarning("Early exit: no TmdbId or ImdbId for {Name}", item.Name);
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
                return Array.Empty<MediaSegmentDto>();
            }
        }

        _logger.LogInformation("Fetching from TheIntroDB API: tmdbId={TmdbId}, imdbId={ImdbId}, isMovie={IsMovie}, season={Season}, episode={Episode}", tmdbId, imdbId, isMovie, season, episode);
        var httpClient = _httpClientFactory.CreateClient();
        var client = new TheIntroDbClient(httpClient, Plugin.Instance, _logger);
        var media = await client.GetMediaAsync(tmdbId, imdbId, isMovie, season, episode, cancellationToken).ConfigureAwait(false);
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
