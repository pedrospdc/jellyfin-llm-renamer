using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LLMRenamer.Services;

/// <summary>
/// LLM service implementation using LLamaSharp.
/// </summary>
public class LlamaSharpService : ILlmService
{
    private readonly ILogger<LlamaSharpService> _logger;
    private readonly object _lock = new();
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaSharpService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public LlamaSharpService(ILogger<LlamaSharpService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsModelLoaded => _model != null && _context != null;

    /// <inheritdoc />
    public async Task LoadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path cannot be empty.", nameof(modelPath));
        }

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Model file not found: {modelPath}");
        }

        await Task.Run(() =>
        {
            lock (_lock)
            {
                UnloadModelInternal();

                var config = Plugin.Instance?.Configuration;
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = config?.ContextSize ?? 2048,
                    GpuLayerCount = config?.GpuLayerCount ?? 0,
                };

                _logger.LogInformation("Loading model from {ModelPath} with context size {ContextSize}",
                    modelPath, parameters.ContextSize);

                _model = LLamaWeights.LoadFromFile(parameters);
                _context = _model.CreateContext(parameters);

                _logger.LogInformation("Model loaded successfully");
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!IsModelLoaded || _context == null || _model == null)
        {
            throw new InvalidOperationException("Model is not loaded. Call LoadModelAsync first.");
        }

        var config = Plugin.Instance?.Configuration;
        var maxTokens = config?.MaxTokens ?? 256;

        var executor = new StatelessExecutor(_model, _context.Params);

        var samplingPipeline = new DefaultSamplingPipeline
        {
            Temperature = 0.3f,
            TopP = 0.9f,
            TopK = 40,
        };

        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = new[] { "\n\n", "```", "</s>", "<|end|>", "<end_of_turn>" },
            SamplingPipeline = samplingPipeline,
        };

        var result = new StringBuilder();

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            result.Append(token);
        }

        return result.ToString().Trim();
    }

    /// <inheritdoc />
    public void UnloadModel()
    {
        lock (_lock)
        {
            UnloadModelInternal();
        }
    }

    private void UnloadModelInternal()
    {
        _context?.Dispose();
        _context = null;
        _model?.Dispose();
        _model = null;
        _logger.LogInformation("Model unloaded");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    /// <param name="disposing">Whether disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            UnloadModel();
        }

        _disposed = true;
    }
}
