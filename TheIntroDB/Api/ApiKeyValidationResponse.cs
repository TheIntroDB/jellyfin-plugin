namespace TheIntroDB.Api;

/// <summary>
/// Response payload for server-side API key validation.
/// </summary>
public sealed class ApiKeyValidationResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the API key is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the validation error, if any.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the user stats for valid API keys.
    /// </summary>
    public TheIntroDbUserStats? Stats { get; set; }

    /// <summary>
    /// Gets or sets the upstream status code for diagnostics.
    /// </summary>
    public int StatusCode { get; set; }
}
