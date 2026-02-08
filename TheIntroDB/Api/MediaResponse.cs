using System.Text.Json.Serialization;

namespace TheIntroDB.Api;

/// <summary>
/// Response from GET /media (TheIntroDB API).
/// </summary>
public class MediaResponse
{
    /// <summary>
    /// Gets or sets the TMDB ID.
    /// </summary>
    [JsonPropertyName("tmdb_id")]
    public int TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the type: "movie" or "tv".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number (TV only).
    /// </summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number (TV only).
    /// </summary>
    [JsonPropertyName("episode")]
    public int? Episode { get; set; }

    /// <summary>
    /// Gets or sets the intro segment (always present, may have null values).
    /// </summary>
    [JsonPropertyName("intro")]
    public SegmentTimestamp? Intro { get; set; }

    /// <summary>
    /// Gets or sets the recap segment (always present, may have null values).
    /// </summary>
    [JsonPropertyName("recap")]
    public SegmentTimestamp? Recap { get; set; }

    /// <summary>
    /// Gets or sets the credits segment (always present, may have null values).
    /// </summary>
    [JsonPropertyName("credits")]
    public SegmentTimestamp? Credits { get; set; }

    /// <summary>
    /// Gets or sets the preview segment (always present, may have null values).
    /// </summary>
    [JsonPropertyName("preview")]
    public SegmentTimestamp? Preview { get; set; }
}
