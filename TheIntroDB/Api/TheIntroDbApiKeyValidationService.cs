using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TheIntroDB.Api;

/// <summary>
/// Validates TheIntroDB API keys server-side and returns user stats for valid tokens.
/// </summary>
public sealed class TheIntroDbApiKeyValidationService
{
    // v2 and v3 no longer serve /user/stats (they are frozen at GET /media), so the
    // key probe has to speak v4 as well.
    private static readonly Uri UserStatsUri = new(TheIntroDbClient.BaseUrl + "/user/stats", UriKind.Absolute);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TheIntroDbApiKeyValidationService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    public TheIntroDbApiKeyValidationService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Validates a TheIntroDB API key and returns stats for valid keys.
    /// </summary>
    /// <param name="apiKey">The API key to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation response.</returns>
    public async Task<ApiKeyValidationResponse> ValidateAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiKeyValidationResponse
            {
                Error = "API key is required.",
                StatusCode = 400
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UserStatsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var httpClient = _httpClientFactory.CreateClient();
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var stats = DeserializeStats(responseBody);

        if (response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(stats?.Error))
        {
            return new ApiKeyValidationResponse
            {
                IsValid = true,
                Stats = stats ?? new TheIntroDbUserStats(),
                StatusCode = (int)response.StatusCode
            };
        }

        return new ApiKeyValidationResponse
        {
            Error = stats?.Error ?? "Invalid or expired token",
            StatusCode = (int)response.StatusCode
        };
    }

    private static TheIntroDbUserStats? DeserializeStats(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TheIntroDbUserStats>(responseBody, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
