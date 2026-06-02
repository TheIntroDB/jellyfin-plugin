using MediaBrowser.Model.Plugins;

namespace TheIntroDB.Configuration;

/// <summary>
/// Plugin configuration for TheIntroDB integration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ApiKey = string.Empty;
        SelectedShowId = string.Empty;
        SelectedShowIds = string.Empty;
        SelectedLibraryIds = string.Empty;
        EnableIntro = true;
        EnableRecap = true;
        EnableCredits = true;
        EnablePreview = true;
        IgnoreMediaWithExistingSegments = true;
        EnableAnonymousUsageReporting = true;
    }

    /// <summary>
    /// Gets or sets the optional API key for TheIntroDB (Bearer token). When set, your pending and accepted submissions are weighted higher.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the optional Jellyfin series id to limit scans to a single show.
    /// When empty, all supported media can be scanned.
    /// </summary>
    public string SelectedShowId { get; set; }

    /// <summary>
    /// Gets or sets the optional comma-separated Jellyfin series ids to limit scans to specific shows.
    /// When empty, all supported media can be scanned.
    /// </summary>
    public string SelectedShowIds { get; set; }

    /// <summary>
    /// Gets or sets the optional comma-separated Jellyfin library or folder ids to whitelist entire libraries.
    /// When empty, no full-library whitelist is applied.
    /// </summary>
    public string SelectedLibraryIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to provide intro segments from TheIntroDB.
    /// </summary>
    public bool EnableIntro { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to provide recap segments from TheIntroDB.
    /// </summary>
    public bool EnableRecap { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to provide credits (outro) segments from TheIntroDB.
    /// </summary>
    public bool EnableCredits { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to provide preview segments from TheIntroDB.
    /// </summary>
    public bool EnablePreview { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to skip API requests for media that already has segments.
    /// When enabled (default), items with existing segments are not refetched from TheIntroDB, saving API rate limit.
    /// </summary>
    public bool IgnoreMediaWithExistingSegments { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether anonymous usage reporting is enabled.
    /// </summary>
    public bool EnableAnonymousUsageReporting { get; set; }
}
