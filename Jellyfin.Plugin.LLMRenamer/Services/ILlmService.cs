namespace Jellyfin.Plugin.LLMRenamer.Services;

/// <summary>
/// Interface for LLM inference service.
/// </summary>
public interface ILlmService : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the model is loaded and ready.
    /// </summary>
    bool IsModelLoaded { get; }

    /// <summary>
    /// Loads the LLM model from the configured path.
    /// </summary>
    /// <param name="modelPath">Path to the GGUF model file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a response from the LLM.
    /// </summary>
    /// <param name="prompt">The prompt to send to the model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated text response.</returns>
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads the current model to free memory.
    /// </summary>
    void UnloadModel();
}
