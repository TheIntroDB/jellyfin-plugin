using System.Text.Json.Serialization;

namespace TheIntroDB.Api;

/// <summary>
/// Segment timestamp from TheIntroDB API (start_ms/end_ms in milliseconds).
/// </summary>
public class SegmentTimestamp
{
    /// <summary>
    /// Gets or sets the start time in milliseconds, or null if segment starts at beginning.
    /// </summary>
    [JsonPropertyName("start_ms")]
    public long? StartMs { get; set; }

    /// <summary>
    /// Gets or sets the end time in milliseconds, or null for end-of-media.
    /// </summary>
    [JsonPropertyName("end_ms")]
    public long? EndMs { get; set; }

    /// <summary>
    /// Gets or sets the confidence score 0–1, or null.
    /// </summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    /// <summary>
    /// Gets or sets the number of submissions used.
    /// </summary>
    [JsonPropertyName("submission_count")]
    public int SubmissionCount { get; set; }

    /// <summary>
    /// Returns whether this segment has usable start and end (for intro/recap both required; for credits/preview start required).
    /// </summary>
    /// <param name="endRequired">True if end time is required (intro/recap).</param>
    /// <returns>True if the segment has a valid range.</returns>
    public bool HasValidRange(bool endRequired)
    {
        if (StartMs is null)
        {
            return false;
        }

        if (endRequired)
        {
            return EndMs.HasValue && EndMs.Value > (StartMs ?? 0);
        }

        return true;
    }
}
