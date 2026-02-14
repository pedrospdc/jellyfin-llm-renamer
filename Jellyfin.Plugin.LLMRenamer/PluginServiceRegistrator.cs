using Jellyfin.Plugin.LLMRenamer.EventHandlers;
using Jellyfin.Plugin.LLMRenamer.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LLMRenamer;

/// <summary>
/// Registers plugin services with the DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("ModelDownload");
        serviceCollection.AddSingleton<ILlmService, LlamaSharpService>();
        serviceCollection.AddSingleton<FileRenamerService>();
        serviceCollection.AddSingleton<ModelDownloadService>();
        serviceCollection.AddHostedService<LibraryChangedHandler>();
    }
}
