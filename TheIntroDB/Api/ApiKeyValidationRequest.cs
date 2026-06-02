namespace TheIntroDB.Api;

/// <summary>
/// Request payload for server-side API key validation.
/// </summary>
public sealed class ApiKeyValidationRequest
{
    /// <summary>
    /// Gets or sets the API key to validate.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
