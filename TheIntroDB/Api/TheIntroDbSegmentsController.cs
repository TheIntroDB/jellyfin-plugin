using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
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
            var segmentsBefore = 0;
            var itemName = "Unknown";

            try
            {
                var item = _libraryManager.GetItemById(itemId);
                if (item is not null)
                {
                    itemName = item.Name ?? "Unknown";

                    var existingSegments = await _mediaSegmentManager
                        .GetSegmentsAsync(item, null, new LibraryOptions(), false)
                        .ConfigureAwait(false);

                    segmentsBefore = existingSegments.Count();
                }
            }
            catch
            {
            }

            try
            {
                await _mediaSegmentManager
                    .DeleteSegmentsAsync(itemId, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation("TheIntroDB deleted segments for {Name} ({Id})", itemName, itemId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TheIntroDB failed to delete segments for {Name} ({Id})", itemName, itemId);
            }

            response.TotalSegmentsRemoved += segmentsBefore;
            response.Results.Add(new ItemResult
            {
                Id = itemId.ToString(),
                Name = itemName,
                SegmentsRemoved = segmentsBefore
            });
        }

        return Ok(response);
    }
}
