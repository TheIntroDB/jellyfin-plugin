using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TheIntroDB.Api;

/// <summary>
/// Plugin API endpoints for managing the TheIntroDB not-found cache.
/// </summary>
[ApiController]
[Route("Plugins/TheIntroDB/NotFoundCache")]
[Produces(MediaTypeNames.Application.Json)]
public class TheIntroDbNotFoundCacheController : ControllerBase
{
    /// <summary>
    /// Clears all cached not-found lookups so the next scan re-checks every item.
    /// </summary>
    /// <returns>The number of cache entries that were cleared.</returns>
    [HttpPost("Clear")]
    [ProducesResponseType(typeof(NotFoundCacheClearResponse), StatusCodes.Status200OK)]
    public ActionResult<NotFoundCacheClearResponse> Clear()
    {
        var cleared = TheIntroDbNotFoundCache.Instance.Clear();
        return Ok(new NotFoundCacheClearResponse { ClearedEntries = cleared });
    }
}
