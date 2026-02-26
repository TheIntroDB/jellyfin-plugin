using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using TheIntroDB.Providers;

namespace TheIntroDB;

/// <summary>
/// Registers plugin services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IMediaSegmentProvider, TheIntroDbSegmentProvider>();
    }
}
