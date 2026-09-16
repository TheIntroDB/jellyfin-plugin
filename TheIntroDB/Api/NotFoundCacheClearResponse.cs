namespace TheIntroDB.Api;

/// <summary>
/// Response for clearing the TheIntroDB not-found cache.
/// </summary>
public sealed class NotFoundCacheClearResponse
{
    /// <summary>
    /// Gets or sets the number of cache entries that were cleared.
    /// </summary>
    public int ClearedEntries { get; set; }
}
