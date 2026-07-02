namespace TheIntroDB.Api;

/// <summary>
/// Result for a single item in the delete segments operation.
/// </summary>
public sealed class ItemResult
{
    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the item.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of segments removed from this item.
    /// </summary>
    public int SegmentsRemoved { get; set; }
}
