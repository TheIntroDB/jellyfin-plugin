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
        EnableIntro = true;
        EnableRecap = true;
        EnableCredits = true;
        EnablePreview = true;
    }

    /// <summary>
    /// Gets or sets the optional API key for TheIntroDB (Bearer token). When set, your pending and accepted submissions are weighted higher.
    /// </summary>
    public string ApiKey { get; set; }

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
}
