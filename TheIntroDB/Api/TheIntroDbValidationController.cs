using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TheIntroDB.Api;

/// <summary>
/// Plugin API endpoints for TheIntroDB validation flows.
/// </summary>
[ApiController]
[Route("Plugins/TheIntroDB/Validation")]
[Produces(MediaTypeNames.Application.Json)]
public class TheIntroDbValidationController : ControllerBase
{
    private readonly TheIntroDbApiKeyValidationService _validationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="TheIntroDbValidationController"/> class.
    /// </summary>
    /// <param name="validationService">The API key validation service.</param>
    public TheIntroDbValidationController(TheIntroDbApiKeyValidationService validationService)
    {
        _validationService = validationService;
    }

    /// <summary>
    /// Validates an API key against the TheIntroDB stats endpoint.
    /// </summary>
    /// <param name="request">The validation request payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation result.</returns>
    [HttpPost("ApiKeyStats")]
    [ProducesResponseType(typeof(ApiKeyValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiKeyValidationResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiKeyValidationResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiKeyValidationResponse>> ValidateApiKeyStats(
        [FromBody, Required] ApiKeyValidationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _validationService.ValidateAsync(request.ApiKey, cancellationToken).ConfigureAwait(false);

        return result.StatusCode switch
        {
            StatusCodes.Status200OK => Ok(result),
            StatusCodes.Status400BadRequest => BadRequest(result),
            StatusCodes.Status401Unauthorized => Unauthorized(result),
            _ => StatusCode(StatusCodes.Status502BadGateway, result)
        };
    }
}
