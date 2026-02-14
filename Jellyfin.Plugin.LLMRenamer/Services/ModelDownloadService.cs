using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LLMRenamer.Services;

/// <summary>
/// Service for downloading GGUF models from Hugging Face.
/// </summary>
public class ModelDownloadService : IDisposable
{
    private readonly ILogger<ModelDownloadService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _modelsDirectory;
    private readonly object _lock = new();

    private CancellationTokenSource? _downloadCts;
    private Task? _downloadTask;
    private DownloadProgress? _currentDownload;
    private bool _disposed;

    /// <summary>
    /// Predefined models available for download.
    /// </summary>
    public static readonly IReadOnlyList<ModelInfo> AvailableModels = new List<ModelInfo>
    {
        new("gemma-3-1b-it-q4_0", "Google Gemma 3 1B Instruct (Q4_0)",
            "google/gemma-3-1b-it-qf4_0-GGUF", "gemma-3-1b-it-q4_0.gguf",
            714_000_000, "Small, fast, good for basic renaming"),

        new("gemma-3-4b-it-q4_0", "Google Gemma 3 4B Instruct (Q4_0)",
            "google/gemma-3-4b-it-qf4_0-GGUF", "gemma-3-4b-it-q4_0.gguf",
            2_500_000_000, "Better quality, requires more RAM"),

        new("phi-3-mini-4k-instruct-q4", "Microsoft Phi-3 Mini 4K (Q4_K_M)",
            "microsoft/Phi-3-mini-4k-instruct-gguf", "Phi-3-mini-4k-instruct-q4.gguf",
            2_300_000_000, "Good balance of speed and quality"),

        new("qwen2.5-1.5b-instruct-q4_k_m", "Qwen 2.5 1.5B Instruct (Q4_K_M)",
            "Qwen/Qwen2.5-1.5B-Instruct-GGUF", "qwen2.5-1.5b-instruct-q4_k_m.gguf",
            1_000_000_000, "Compact model with good instruction following"),

        new("llama-3.2-1b-instruct-q4_k_m", "Meta Llama 3.2 1B Instruct (Q4_K_M)",
            "bartowski/Llama-3.2-1B-Instruct-GGUF", "Llama-3.2-1B-Instruct-Q4_K_M.gguf",
            800_000_000, "Fast and efficient, good for simple tasks"),
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelDownloadService"/> class.
    /// </summary>
    public ModelDownloadService(ILogger<ModelDownloadService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("ModelDownload");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-LLM-Renamer/1.0");
        _httpClient.Timeout = TimeSpan.FromHours(2); // Allow long downloads

        var pluginDataPath = Plugin.Instance?.DataFolderPath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jellyfin", "plugins", "LLMRenamer");
        _modelsDirectory = Path.Combine(pluginDataPath, "models");

        Directory.CreateDirectory(_modelsDirectory);
    }

    /// <summary>
    /// Gets the models directory path.
    /// </summary>
    public string ModelsDirectory => _modelsDirectory;

    /// <summary>
    /// Gets the current download progress.
    /// </summary>
    public DownloadProgress? CurrentDownload
    {
        get
        {
            lock (_lock)
            {
                return _currentDownload;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a download is in progress.
    /// </summary>
    public bool IsDownloading
    {
        get
        {
            lock (_lock)
            {
                return _downloadTask != null && !_downloadTask.IsCompleted;
            }
        }
    }

    /// <summary>
    /// Gets a list of locally available models.
    /// </summary>
    public List<LocalModelInfo> GetLocalModels()
    {
        var models = new List<LocalModelInfo>();

        if (!Directory.Exists(_modelsDirectory))
        {
            return models;
        }

        foreach (var file in Directory.GetFiles(_modelsDirectory, "*.gguf"))
        {
            var fileInfo = new FileInfo(file);
            var knownModel = AvailableModels.FirstOrDefault(m =>
                m.Filename.Equals(fileInfo.Name, StringComparison.OrdinalIgnoreCase));

            models.Add(new LocalModelInfo(
                fileInfo.Name,
                file,
                fileInfo.Length,
                knownModel?.DisplayName ?? fileInfo.Name,
                fileInfo.LastWriteTimeUtc
            ));
        }

        return models;
    }

    /// <summary>
    /// Starts downloading a model in the background. Returns immediately.
    /// </summary>
    public bool StartDownload(string modelId)
    {
        var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
        if (model == null)
        {
            throw new ArgumentException($"Unknown model: {modelId}");
        }

        return StartDownloadInternal(model.Id, model.DisplayName, model.Filename,
            $"https://huggingface.co/{model.HuggingFaceRepo}/resolve/main/{model.Filename}",
            model.ExpectedSize);
    }

    /// <summary>
    /// Starts downloading a custom model in the background. Returns immediately.
    /// </summary>
    public bool StartCustomDownload(string url, string filename)
    {
        if (!filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".gguf";
        }

        return StartDownloadInternal("custom", filename, filename, url, 0);
    }

    private bool StartDownloadInternal(string modelId, string displayName, string filename, string url, long expectedSize)
    {
        lock (_lock)
        {
            if (IsDownloading)
            {
                _logger.LogWarning("Download already in progress");
                return false;
            }

            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            _currentDownload = new DownloadProgress(
                modelId, displayName, 0, expectedSize,
                DownloadState.Starting, "Initializing...", 0, null, null);

            _downloadTask = Task.Run(async () =>
            {
                await DownloadFileAsync(modelId, displayName, filename, url, expectedSize, token);
            }, token);

            return true;
        }
    }

    private async Task DownloadFileAsync(string modelId, string displayName, string filename, string url, long expectedSize, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(_modelsDirectory, filename);
        var startTime = DateTime.UtcNow;

        try
        {
            // Check if already exists
            if (File.Exists(outputPath))
            {
                var existingSize = new FileInfo(outputPath).Length;
                if (expectedSize > 0 && existingSize >= expectedSize * 0.95)
                {
                    UpdateProgress(modelId, displayName, existingSize, existingSize,
                        DownloadState.Completed, "Already downloaded", 100, null, outputPath);
                    AutoConfigureModel(outputPath, displayName);
                    return;
                }
                File.Delete(outputPath);
            }

            _logger.LogInformation("Starting download of {ModelId} from {Url}", modelId, url);

            UpdateProgress(modelId, displayName, 0, expectedSize,
                DownloadState.Downloading, "Connecting...", 0, null, null);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
            if (totalBytes == 0) totalBytes = expectedSize;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long downloadedBytes = 0;
            int bytesRead;
            var lastUpdate = DateTime.UtcNow;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                var now = DateTime.UtcNow;
                if ((now - lastUpdate).TotalMilliseconds >= 250)
                {
                    var elapsed = (now - startTime).TotalSeconds;
                    var speed = elapsed > 0 ? downloadedBytes / elapsed : 0;
                    var percentage = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
                    var remaining = speed > 0 && totalBytes > 0
                        ? TimeSpan.FromSeconds((totalBytes - downloadedBytes) / speed)
                        : (TimeSpan?)null;

                    var status = $"{FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)} ({FormatSpeed(speed)})";

                    UpdateProgress(modelId, displayName, downloadedBytes, totalBytes,
                        DownloadState.Downloading, status, percentage, remaining, null);

                    lastUpdate = now;
                }
            }

            await fileStream.FlushAsync(cancellationToken);

            _logger.LogInformation("Download complete: {Path}", outputPath);

            UpdateProgress(modelId, displayName, downloadedBytes, totalBytes,
                DownloadState.Completed, "Download complete!", 100, null, outputPath);

            AutoConfigureModel(outputPath, displayName);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Download cancelled: {ModelId}", modelId);
            UpdateProgress(modelId, displayName, 0, expectedSize,
                DownloadState.Cancelled, "Download cancelled", 0, null, null);
            CleanupPartialDownload(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed: {ModelId}", modelId);
            UpdateProgress(modelId, displayName, 0, expectedSize,
                DownloadState.Failed, $"Error: {ex.Message}", 0, null, null);
            CleanupPartialDownload(outputPath);
        }
    }

    private void UpdateProgress(string modelId, string displayName, long downloaded, long total,
        DownloadState state, string status, double percentage, TimeSpan? eta, string? completedPath)
    {
        lock (_lock)
        {
            _currentDownload = new DownloadProgress(
                modelId, displayName, downloaded, total,
                state, status, percentage, eta, completedPath);
        }
    }

    private void AutoConfigureModel(string modelPath, string displayName)
    {
        var config = Plugin.Instance?.Configuration;
        if (config != null && string.IsNullOrEmpty(config.ModelPath))
        {
            config.ModelPath = modelPath;
            config.ModelName = displayName;
            Plugin.Instance?.SaveConfiguration();
            _logger.LogInformation("Auto-configured model: {Path}", modelPath);
        }
    }

    private void CleanupPartialDownload(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup partial download: {Path}", path);
        }
    }

    /// <summary>
    /// Cancels the current download.
    /// </summary>
    public void CancelDownload()
    {
        lock (_lock)
        {
            _downloadCts?.Cancel();
        }
    }

    /// <summary>
    /// Clears the completed/failed download status.
    /// </summary>
    public void ClearDownloadStatus()
    {
        lock (_lock)
        {
            if (_currentDownload?.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled)
            {
                _currentDownload = null;
                _downloadTask = null;
            }
        }
    }

    /// <summary>
    /// Deletes a local model.
    /// </summary>
    public bool DeleteModel(string filename)
    {
        var path = Path.Combine(_modelsDirectory, filename);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted model {Filename}", filename);
            return true;
        }
        return false;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.#} {sizes[order]}";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / 1024 / 1024:0.#} MB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024:0.#} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _httpClient.Dispose();

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Information about an available model.
/// </summary>
public record ModelInfo(
    string Id,
    string DisplayName,
    string HuggingFaceRepo,
    string Filename,
    long ExpectedSize,
    string Description
);

/// <summary>
/// Information about a locally downloaded model.
/// </summary>
public record LocalModelInfo(
    string Filename,
    string FullPath,
    long Size,
    string DisplayName,
    DateTime DownloadedAt
);

/// <summary>
/// Download state enumeration.
/// </summary>
public enum DownloadState
{
    Starting,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Download progress information.
/// </summary>
public record DownloadProgress(
    string ModelId,
    string DisplayName,
    long DownloadedBytes,
    long TotalBytes,
    DownloadState State,
    string Status,
    double Percentage,
    TimeSpan? EstimatedTimeRemaining,
    string? CompletedPath
);
