using System;
using System.Collections.Generic;
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
using TheIntroDB.Api;
using TheIntroDB.Configuration;

namespace TheIntroDB.Providers;

/// <summary>
/// Media segment provider that fetches intro/recap/credits/preview from TheIntroDB API and returns Jellyfin media segments.
/// </summary>
public class TheIntroDbSegmentProvider : IMediaSegmentProvider
{
    private readonly HttpClient _httpClient;
    private readonly Plugin _plugin;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TheIntroDbSegmentProvider"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client for API requests.</param>
    /// <param name="plugin">The plugin instance for configuration.</param>
    /// <param name="libraryManager">Library manager to resolve items.</param>
    public TheIntroDbSegmentProvider(
        HttpClient httpClient,
        Plugin plugin,
        ILibraryManager libraryManager)
    {
        _httpClient = httpClient;
        _plugin = plugin;
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public string Name => _plugin.Name ?? "TheIntroDB";

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(
        MediaSegmentGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Array.Empty<MediaSegmentDto>();
        }

        if (_plugin.Configuration is not PluginConfiguration config)
        {
            return Array.Empty<MediaSegmentDto>();
        }

        var item = _libraryManager.GetItemById(request.ItemId);
        if (item is null)
        {
            return Array.Empty<MediaSegmentDto>();
        }

        int? tmdbId = null;
        bool isMovie = false;
        int? season = null;
        int? episode = null;

        if (item is Movie movie)
        {
            isMovie = true;
            tmdbId = GetTmdbId(movie);
        }
        else if (item is Episode ep)
        {
            tmdbId = GetTmdbId(ep.Series);
            season = ep.ParentIndexNumber;
            episode = ep.IndexNumber;
        }

        if (!tmdbId.HasValue || tmdbId.Value <= 0)
        {
            return Array.Empty<MediaSegmentDto>();
        }

        if (!isMovie && (!season.HasValue || !episode.HasValue))
        {
            return Array.Empty<MediaSegmentDto>();
        }

        var client = new TheIntroDbClient(_httpClient, _plugin);
        var media = await client.GetMediaAsync(tmdbId.Value, isMovie, season, episode, cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
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

        return segments;
    }

    /// <inheritdoc />
    public ValueTask<bool> Supports(BaseItem item)
    {
        return ValueTask.FromResult(item is Episode or Movie);
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

    private static bool AddSegment(
        SegmentTimestamp? stamp,
        bool endRequired,
        MediaSegmentType type,
        Guid itemId,
        long? runTimeTicks,
        List<MediaSegmentDto> segments)
    {
        if (stamp is null || !stamp.HasValidRange(endRequired))
        {
            return false;
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
            return false;
        }

        if (endMs <= startMs)
        {
            return false;
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
        return true;
    }
}
