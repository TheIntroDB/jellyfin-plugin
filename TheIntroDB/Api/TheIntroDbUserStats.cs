using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheIntroDB.Api;

/// <summary>
/// User stats returned by the TheIntroDB API.
/// </summary>
public sealed class TheIntroDbUserStats
{
    /// <summary>
    /// Gets or sets the total contribution count.
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets the accepted contribution count.
    /// </summary>
    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }

    /// <summary>
    /// Gets or sets the pending contribution count.
    /// </summary>
    [JsonPropertyName("pending")]
    public int Pending { get; set; }

    /// <summary>
    /// Gets or sets the rejected contribution count.
    /// </summary>
    [JsonPropertyName("rejected")]
    public int Rejected { get; set; }

    /// <summary>
    /// Gets or sets the acceptance rate.
    /// </summary>
    [JsonPropertyName("acceptance_rate")]
    public double AcceptanceRate { get; set; }

    /// <summary>
    /// Gets or sets the current streak.
    /// </summary>
    [JsonPropertyName("current_streak")]
    public int CurrentStreak { get; set; }

    /// <summary>
    /// Gets or sets the best streak.
    /// </summary>
    [JsonPropertyName("best_streak")]
    public int BestStreak { get; set; }

    /// <summary>
    /// Gets or sets the total time saved in milliseconds.
    /// </summary>
    [JsonPropertyName("total_time_saved_ms")]
    public long TotalTimeSavedMs { get; set; }

    /// <summary>
    /// Gets the top media breakdown.
    /// </summary>
    [JsonPropertyName("top_media")]
    public Collection<JsonElement> TopMedia { get; } = [];

    /// <summary>
    /// Gets or sets the upstream API error, when present.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
