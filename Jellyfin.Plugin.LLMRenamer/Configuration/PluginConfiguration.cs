using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LLMRenamer.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the path to the GGUF model file.
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model name for downloading (e.g., "gemma-3-1b-it").
    /// </summary>
    public string ModelName { get; set; } = "gemma-3-1b-it";

    /// <summary>
    /// Gets or sets the number of GPU layers to offload. Set to 0 for CPU-only.
    /// </summary>
    public int GpuLayerCount { get; set; } = 0;

    /// <summary>
    /// Gets or sets the context size for the model.
    /// </summary>
    public uint ContextSize { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the maximum tokens to generate.
    /// </summary>
    public int MaxTokens { get; set; } = 256;

    /// <summary>
    /// Gets or sets whether to enable automatic renaming on library scan.
    /// </summary>
    public bool EnableAutoRename { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to create a preview/dry run before renaming.
    /// </summary>
    public bool PreviewOnly { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to rename movie files.
    /// </summary>
    public bool RenameMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to rename TV show episode files.
    /// </summary>
    public bool RenameEpisodes { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to rename music files.
    /// </summary>
    public bool RenameMusic { get; set; } = false;

    /// <summary>
    /// Gets or sets custom prompt additions for the LLM.
    /// </summary>
    public string CustomPromptAdditions { get; set; } = string.Empty;
}
