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
    /// Returns whether this segment has a usable time range.
    /// For intro/recap, start is optional and end is required.
    /// For credits/preview, start is required and end is optional.
    /// An end at or before the start is never valid: the API sends 0/0 for a segment
    /// confirmed absent, which would otherwise run from the start to the end of the media.
    /// </summary>
    /// <param name="endRequired">True if end time is required (intro/recap).</param>
    /// <returns>True if the segment has a valid range.</returns>
    public bool HasValidRange(bool endRequired)
    {
        if (EndMs.HasValue)
        {
            return EndMs.Value > (StartMs ?? 0);
        }

        return !endRequired && StartMs.HasValue;
    }
}
