using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.MediaSegments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace TheIntroDB.Api;

/// <summary>
/// API endpoints for removing TheIntroDB segments from library items.
/// </summary>
[ApiController]
[Route("Plugins/TheIntroDB/Segments")]
[Produces(MediaTypeNames.Application.Json)]
public class TheIntroDbSegmentsController : ControllerBase
{
    /// <summary>
    /// Display name of this plugin's media segment provider. Must match
    /// <see cref="Plugin.Name"/> (and therefore <c>TheIntroDbSegmentProvider.Name</c>),
    /// because Jellyfin keys stored segments by a provider id derived from this name.
    /// </summary>
    private const string TheIntroDbProviderName = "TheIntroDB";

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager;
    private readonly ILogger<TheIntroDbSegmentsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TheIntroDbSegmentsController"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="mediaSegmentManager">The media segment manager.</param>
    /// <param name="logger">The logger.</param>
    public TheIntroDbSegmentsController(
        ILibraryManager libraryManager,
        IMediaSegmentManager mediaSegmentManager,
        ILogger<TheIntroDbSegmentsController> logger)
    {
        _libraryManager = libraryManager;
        _mediaSegmentManager = mediaSegmentManager;
        _logger = logger;
    }

    /// <summary>
    /// Deletes TheIntroDB segments from the specified items.
    /// </summary>
    /// <param name="request">The request containing item IDs to clean up.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A summary of the deletion operation.</returns>
    [HttpDelete("Chapters")]
    public async Task<ActionResult<DeleteSegmentsResponse>> DeleteSegments(
        [FromBody, Required] DeleteSegmentsRequest request,
        CancellationToken cancellationToken)
    {
        var response = new DeleteSegmentsResponse();

        if (request.ItemIds is null || request.ItemIds.Count == 0)
        {
            return Ok(response);
        }

        var guids = request.ItemIds
            .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToArray();

        if (guids.Length == 0)
        {
            return Ok(response);
        }

        response.TotalItems = guids.Length;

        foreach (var itemId in guids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemName = "Unknown";
            var segmentsRemoved = 0;

            try
            {
                var item = _libraryManager.GetItemById(itemId);
                if (item is null)
                {
                    _logger.LogWarning("TheIntroDB could not resolve item {ItemId}; skipping segment removal", itemId);
                }
                else
                {
                    itemName = item.Name ?? "Unknown";
                    segmentsRemoved = await RemoveTheIntroDbSegmentsAsync(item).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TheIntroDB failed to delete segments for {Name} ({Id})", itemName, itemId);
            }

            response.TotalSegmentsRemoved += segmentsRemoved;
            response.Results.Add(new ItemResult
            {
                Id = itemId.ToString(),
                Name = itemName,
                SegmentsRemoved = segmentsRemoved
            });
        }

        return Ok(response);
    }

    /// <summary>
    /// Deletes only the media segments owned by this plugin for the given item, leaving
    /// segments produced by other providers (e.g. Intro Skipper) completely untouched.
    /// </summary>
    /// <param name="item">The library item whose TheIntroDB segments should be removed.</param>
    /// <returns>The number of TheIntroDB segments removed.</returns>
    private async Task<int> RemoveTheIntroDbSegmentsAsync(BaseItem item)
    {
        var supportedProviders = _mediaSegmentManager.GetSupportedProviders(item).ToArray();

        // Only segments owned by this plugin are ever touched. If this plugin's provider is
        // not registered for the item (non-video item, or provider disabled), there is nothing
        // to remove — and we must NOT fall back to deleting all segments, which would wipe
        // segments produced by other providers (e.g. Intro Skipper).
        if (!supportedProviders.Any(provider => IsTheIntroDbProvider(provider.Name)))
        {
            return 0;
        }

        // GetSegmentsAsync with filterByProvider: true returns only segments whose provider is
        // NOT in DisabledMediaSegmentProviders. Disable every other provider so that only this
        // plugin's segments are returned. Jellyfin 10.11 matches disabled entries against the
        // hashed provider id, while newer servers match the provider name — include both forms.
        var tidbOnlyOptions = new LibraryOptions
        {
            DisabledMediaSegmentProviders = supportedProviders
                .Where(provider => !IsTheIntroDbProvider(provider.Name))
                .SelectMany(provider => new[] { provider.Id, provider.Name })
                .ToArray()
        };

        var tidbSegments = (await _mediaSegmentManager
            .GetSegmentsAsync(item, null, tidbOnlyOptions, filterByProvider: true)
            .ConfigureAwait(false)).ToArray();

        foreach (var segment in tidbSegments)
        {
            await _mediaSegmentManager.DeleteSegmentAsync(segment.Id).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "TheIntroDB removed {Count} of its own segment(s) for {Name} ({Id}); other providers' segments left intact",
            tidbSegments.Length,
            item.Name ?? "Unknown",
            item.Id);

        return tidbSegments.Length;
    }

    private static bool IsTheIntroDbProvider(string providerName)
        => string.Equals(providerName, TheIntroDbProviderName, StringComparison.OrdinalIgnoreCase);
}
