using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TheIntroDB.Api;

/// <summary>
/// Response payload for the delete segments operation.
/// </summary>
public sealed class DeleteSegmentsResponse
{
    /// <summary>
    /// Gets or sets the total number of items processed.
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Gets or sets the total number of segments removed across all items.
    /// </summary>
    public int TotalSegmentsRemoved { get; set; }

    /// <summary>
    /// Gets the per-item results.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<ItemResult> Results { get; } = new();
}
