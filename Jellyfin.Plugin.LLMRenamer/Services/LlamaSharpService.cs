using System.Runtime.InteropServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
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
    private static bool _nativeLibraryConfigured;
    private Timer? _idleUnloadTimer;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="LlamaSharpService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public LlamaSharpService(ILogger<LlamaSharpService> logger)
    {
        _logger = logger;
    }

    private void ConfigureNativeLibrary()
    {
        if (_nativeLibraryConfigured)
        {
            return;
        }

        var dataDir = Plugin.Instance?.SafeDataPath;
        if (string.IsNullOrEmpty(dataDir))
        {
            return;
        }

        _nativeLibraryConfigured = true;

        // Native libs live in the plugin data directory under runtimes/{rid}/native/{variant}/
        // They can't be in the plugin install directory because Jellyfin scans all subdirs
        // and tries to load native DLLs as .NET assemblies, disabling the plugin.
        var platform = GetRuntimePlatform();
        var ext = OperatingSystem.IsWindows() ? ".dll" : ".so";
        var prefix = OperatingSystem.IsWindows() ? "" : "lib";
        var gpuLayers = Plugin.Instance?.Configuration?.GpuLayerCount ?? 0;

        // Check if CUDA12 backend exists and GPU is requested
        var cudaDir = Path.Combine(dataDir, "runtimes", platform, "native", "cuda12");
        var cudaLlama = Path.Combine(cudaDir, $"{prefix}llama{ext}");

        if (gpuLayers > 0 && File.Exists(cudaLlama))
        {
            _logger.LogInformation("CUDA12 backend found at {Path}, attempting to load", cudaDir);
            PluginLog.Info($"CUDA12 backend found, attempting GPU load (layers: {gpuLayers})");

            // ggml-cuda.dll depends on cublas64_12.dll / cudart64_12.dll from the CUDA 12 Toolkit.
            // The toolkit bin dir must be in the DLL search path. We add it proactively because:
            // - CUDA_PATH env var may not be set (e.g., multiple CUDA versions installed)
            // - Jellyfin may have started before the toolkit was installed
            if (OperatingSystem.IsWindows())
            {
                PreloadCuda12ToolkitLibraries();
            }

            // Pre-load CUDA dependencies in correct order.
            // ggml.dll from cuda12 depends on ggml-cpu.dll from the CPU backend (AVX variant),
            // so we must also find and load the best available ggml-cpu.dll.
            var ggmlCpuPath = FindBestGgmlCpu(dataDir, platform, prefix, ext);
            var deps = new List<string>
            {
                Path.Combine(cudaDir, $"{prefix}ggml-base{ext}"),
                Path.Combine(cudaDir, $"{prefix}ggml-cuda{ext}"),
            };
            if (ggmlCpuPath != null)
            {
                deps.Add(ggmlCpuPath);
            }
            deps.Add(Path.Combine(cudaDir, $"{prefix}ggml{ext}"));

            var allLoaded = true;
            foreach (var dep in deps)
            {
                if (!File.Exists(dep))
                {
                    _logger.LogWarning("CUDA dependency not found: {Path}", dep);
                    allLoaded = false;
                    break;
                }

                if (NativeLibrary.TryLoad(dep, out _))
                {
                    _logger.LogInformation("Pre-loaded CUDA dependency: {File}", Path.GetFileName(dep));
                }
                else
                {
                    _logger.LogWarning("Failed to pre-load CUDA dependency: {File}. Missing CUDA 12 toolkit?", Path.GetFileName(dep));
                    PluginLog.Warn($"Failed to load {Path.GetFileName(dep)} - CUDA 12 toolkit may not be installed");
                    allLoaded = false;
                    break;
                }
            }

            if (allLoaded)
            {
                var cudaMtmd = Path.Combine(cudaDir, $"{prefix}mtmd{ext}");
                NativeLibraryConfig.All.WithLibrary(cudaLlama, File.Exists(cudaMtmd) ? cudaMtmd : null);
                _logger.LogInformation("Configured LLamaSharp to use CUDA12 backend");
                PluginLog.Info("CUDA12 backend configured successfully");
                return;
            }

            _logger.LogWarning("CUDA pre-loading failed, falling back to CPU backend");
            PluginLog.Warn("CUDA backend failed to load, falling back to CPU");
        }

        // CPU fallback: use search directory so LLamaSharp finds runtimes/{rid}/native/{avx}/
        _logger.LogInformation("Configuring LLamaSharp search directory: {Path}", dataDir);
        PluginLog.Info($"Using CPU backend (search: {dataDir})");
        NativeLibraryConfig.All.WithSearchDirectory(dataDir);
    }

    /// <summary>
    /// Find CUDA 12 toolkit and pre-load its runtime DLLs (cublas, cudart) so that
    /// ggml-cuda.dll can resolve its dependencies when loaded.
    /// </summary>
    private void PreloadCuda12ToolkitLibraries()
    {
        string? cuda12BinDir = null;

        // Try CUDA_PATH env var first
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath) && cudaPath.Contains("12", StringComparison.Ordinal))
        {
            var binDir = Path.Combine(cudaPath, "bin");
            if (File.Exists(Path.Combine(binDir, "cublas64_12.dll")))
            {
                cuda12BinDir = binDir;
            }
        }

        // Search common installation directories for any CUDA 12.x
        if (cuda12BinDir == null)
        {
            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA GPU Computing Toolkit", "CUDA");

            if (Directory.Exists(basePath))
            {
                foreach (var dir in Directory.GetDirectories(basePath, "v12.*").OrderByDescending(d => d))
                {
                    var binDir = Path.Combine(dir, "bin");
                    if (File.Exists(Path.Combine(binDir, "cublas64_12.dll")))
                    {
                        cuda12BinDir = binDir;
                        break;
                    }
                }
            }
        }

        if (cuda12BinDir == null)
        {
            _logger.LogWarning("CUDA 12 toolkit not found in standard locations");
            return;
        }

        _logger.LogInformation("Found CUDA 12 toolkit bin: {Path}", cuda12BinDir);
        PluginLog.Info($"CUDA 12 toolkit found: {cuda12BinDir}");

        // Pre-load the CUDA runtime DLLs that ggml-cuda.dll depends on
        foreach (var dllName in new[] { "cublas64_12.dll", "cublasLt64_12.dll", "cudart64_12.dll" })
        {
            var dllPath = Path.Combine(cuda12BinDir, dllName);
            if (File.Exists(dllPath))
            {
                if (NativeLibrary.TryLoad(dllPath, out _))
                {
                    _logger.LogInformation("Pre-loaded CUDA toolkit library: {File}", dllName);
                }
                else
                {
                    _logger.LogWarning("Failed to pre-load CUDA toolkit library: {File}", dllName);
                }
            }
        }
    }

    /// <summary>
    /// Find the best available ggml-cpu DLL from the CPU backend's AVX variants.
    /// </summary>
    private string? FindBestGgmlCpu(string dataDir, string platform, string prefix, string ext)
    {
        var nativeDir = Path.Combine(dataDir, "runtimes", platform, "native");
        // Try AVX levels from best to worst
        foreach (var variant in new[] { "avx512", "avx2", "avx", "noavx" })
        {
            var path = Path.Combine(nativeDir, variant, $"{prefix}ggml-cpu{ext}");
            if (File.Exists(path))
            {
                _logger.LogInformation("Found ggml-cpu from CPU backend: {Variant}", variant);
                return path;
            }
        }

        _logger.LogWarning("No ggml-cpu.dll found - CPU backend may not be downloaded");
        return null;
    }

    private static string GetRuntimePlatform()
    {
        if (OperatingSystem.IsWindows()) return "win-x64";
        if (OperatingSystem.IsLinux()) return "linux-x64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return "unknown";
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

        ConfigureNativeLibrary();

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
                PluginLog.Info($"Loading model: {Path.GetFileName(modelPath)} (context: {parameters.ContextSize}, GPU layers: {parameters.GpuLayerCount})");

                _model = LLamaWeights.LoadFromFile(parameters);
                _context = _model.CreateContext(parameters);

                _logger.LogInformation("Model loaded successfully");
                PluginLog.Info("Model loaded successfully");
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

        ResetIdleTimer();

        var config = Plugin.Instance?.Configuration;
        var maxTokens = config?.MaxTokens ?? 256;

        // Wrap in Qwen/ChatML chat template so instruction-tuned models follow instructions
        var chatPrompt = $"<|im_start|>system\nYou are a file renaming assistant. You respond with ONLY the new filename, nothing else.<|im_end|>\n<|im_start|>user\n{prompt}<|im_end|>\n<|im_start|>assistant\n";

        var executor = new StatelessExecutor(_model, _context.Params);

        var samplingPipeline = new DefaultSamplingPipeline
        {
            Temperature = 0.1f,
            TopP = 0.9f,
            TopK = 40,
        };

        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = new[] { "<|im_end|>", "<|im_start|>", "\n\n", "```", "</s>", "<|end|>", "<end_of_turn>" },
            SamplingPipeline = samplingPipeline,
        };

        var result = new StringBuilder();

        await foreach (var token in executor.InferAsync(chatPrompt, inferenceParams, cancellationToken))
        {
            result.Append(token);
        }

        return result.ToString().Trim();
    }

    private void ResetIdleTimer()
    {
        _idleUnloadTimer?.Dispose();
        _idleUnloadTimer = new Timer(
            _ => OnIdleTimeout(),
            null,
            IdleTimeout,
            Timeout.InfiniteTimeSpan);
    }

    private void OnIdleTimeout()
    {
        if (!IsModelLoaded) return;
        _logger.LogInformation("Model idle for {Minutes} minutes, auto-unloading to free memory", IdleTimeout.TotalMinutes);
        PluginLog.Info($"Auto-unloading model after {IdleTimeout.TotalMinutes} minutes idle");
        UnloadModel();
    }

    /// <inheritdoc />
    public void UnloadModel()
    {
        lock (_lock)
        {
            _idleUnloadTimer?.Dispose();
            _idleUnloadTimer = null;
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
        PluginLog.Info("Model unloaded");
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
            _idleUnloadTimer?.Dispose();
            _idleUnloadTimer = null;
            UnloadModel();
        }

        _disposed = true;
    }
}
