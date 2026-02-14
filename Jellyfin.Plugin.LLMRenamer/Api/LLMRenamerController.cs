using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Jellyfin.Plugin.LLMRenamer.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LLMRenamer.Api;

/// <summary>
/// API controller for LLM Renamer operations.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("LLMRenamer")]
[Produces(MediaTypeNames.Application.Json)]
public class LLMRenamerController : ControllerBase
{
    private readonly ILogger<LLMRenamerController> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly ILlmService _llmService;
    private readonly FileRenamerService _renamerService;
    private readonly ModelDownloadService _modelDownloadService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LLMRenamerController"/> class.
    /// </summary>
    public LLMRenamerController(
        ILogger<LLMRenamerController> logger,
        ILibraryManager libraryManager,
        ILlmService llmService,
        FileRenamerService renamerService,
        ModelDownloadService modelDownloadService)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _llmService = llmService;
        _renamerService = renamerService;
        _modelDownloadService = modelDownloadService;
    }

    #region Model Status & Loading

    /// <summary>
    /// Gets the current model status.
    /// </summary>
    /// <returns>Model status information.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ModelStatusResponse> GetStatus()
    {
        var config = Plugin.Instance?.Configuration;
        var currentDownload = _modelDownloadService.CurrentDownload;

        return Ok(new ModelStatusResponse
        {
            IsModelLoaded = _llmService.IsModelLoaded,
            ModelPath = config?.ModelPath ?? string.Empty,
            ModelName = config?.ModelName ?? "Not configured",
            ModelsDirectory = _modelDownloadService.ModelsDirectory,
            IsDownloading = currentDownload != null,
            DownloadProgress = currentDownload
        });
    }

    /// <summary>
    /// Loads the configured model.
    /// </summary>
    /// <returns>Result of the load operation.</returns>
    [HttpPost("LoadModel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> LoadModel(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || string.IsNullOrEmpty(config.ModelPath))
        {
            return BadRequest("Model path is not configured");
        }

        try
        {
            await _llmService.LoadModelAsync(config.ModelPath, cancellationToken);
            return Ok(new { Message = "Model loaded successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model");
            return BadRequest($"Failed to load model: {ex.Message}");
        }
    }

    /// <summary>
    /// Unloads the current model.
    /// </summary>
    /// <returns>Result of the unload operation.</returns>
    [HttpPost("UnloadModel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult UnloadModel()
    {
        _llmService.UnloadModel();
        return Ok(new { Message = "Model unloaded" });
    }

    #endregion

    #region Model Management

    /// <summary>
    /// Gets the list of available models for download.
    /// </summary>
    /// <returns>Available models.</returns>
    [HttpGet("Models/Available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<ModelInfo>> GetAvailableModels()
    {
        return Ok(ModelDownloadService.AvailableModels);
    }

    /// <summary>
    /// Gets the list of locally downloaded models.
    /// </summary>
    /// <returns>Local models.</returns>
    [HttpGet("Models/Local")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<LocalModelInfo>> GetLocalModels()
    {
        return Ok(_modelDownloadService.GetLocalModels());
    }

    /// <summary>
    /// Starts downloading a predefined model in the background.
    /// Poll /Models/DownloadProgress for status updates.
    /// </summary>
    /// <param name="modelId">The model ID to download.</param>
    /// <returns>Result indicating download started.</returns>
    [HttpPost("Models/Download/{**modelId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult StartDownload([FromRoute, Required] string modelId)
    {
        try
        {
            if (_modelDownloadService.IsDownloading)
            {
                return Conflict(new { Message = "A download is already in progress" });
            }

            var started = _modelDownloadService.StartDownload(modelId);

            if (started)
            {
                return Ok(new { Message = "Download started", ModelId = modelId });
            }

            return BadRequest(new { Message = "Failed to start download" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Starts downloading a custom model from a URL in the background.
    /// Poll /Models/DownloadProgress for status updates.
    /// </summary>
    /// <param name="request">Download request.</param>
    /// <returns>Result indicating download started.</returns>
    [HttpPost("Models/DownloadCustom")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult StartCustomDownload([FromBody] CustomModelDownloadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { Message = "URL is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Filename))
        {
            return BadRequest(new { Message = "Filename is required" });
        }

        if (_modelDownloadService.IsDownloading)
        {
            return Conflict(new { Message = "A download is already in progress" });
        }

        var started = _modelDownloadService.StartCustomDownload(request.Url, request.Filename);

        if (started)
        {
            return Ok(new { Message = "Download started", Filename = request.Filename });
        }

        return BadRequest(new { Message = "Failed to start download" });
    }

    /// <summary>
    /// Gets the current download progress.
    /// </summary>
    /// <returns>Download progress or null if no download.</returns>
    [HttpGet("Models/DownloadProgress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<DownloadProgressResponse> GetDownloadProgress()
    {
        var progress = _modelDownloadService.CurrentDownload;
        if (progress == null)
        {
            return Ok(new DownloadProgressResponse { IsDownloading = false });
        }

        return Ok(new DownloadProgressResponse
        {
            IsDownloading = progress.State == DownloadState.Starting || progress.State == DownloadState.Downloading,
            ModelId = progress.ModelId,
            DisplayName = progress.DisplayName,
            DownloadedBytes = progress.DownloadedBytes,
            TotalBytes = progress.TotalBytes,
            State = progress.State.ToString(),
            Status = progress.Status,
            Percentage = progress.Percentage,
            EstimatedSecondsRemaining = progress.EstimatedTimeRemaining?.TotalSeconds,
            CompletedPath = progress.CompletedPath
        });
    }

    /// <summary>
    /// Cancels the current download.
    /// </summary>
    /// <returns>Result of the cancel operation.</returns>
    [HttpPost("Models/CancelDownload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult CancelDownload()
    {
        _modelDownloadService.CancelDownload();
        return Ok(new { Message = "Download cancellation requested" });
    }

    /// <summary>
    /// Clears the download status after completion/failure.
    /// </summary>
    /// <returns>Result of the clear operation.</returns>
    [HttpPost("Models/ClearDownloadStatus")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult ClearDownloadStatus()
    {
        _modelDownloadService.ClearDownloadStatus();
        return Ok(new { Message = "Download status cleared" });
    }

    /// <summary>
    /// Deletes a local model.
    /// </summary>
    /// <param name="filename">The filename to delete.</param>
    /// <returns>Result of the delete operation.</returns>
    [HttpDelete("Models/{**filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteModel([FromRoute, Required] string filename)
    {
        // Unload if this is the currently loaded model
        var config = Plugin.Instance?.Configuration;
        if (config?.ModelPath?.EndsWith(filename, StringComparison.OrdinalIgnoreCase) == true)
        {
            _llmService.UnloadModel();
            config.ModelPath = string.Empty;
            Plugin.Instance?.SaveConfiguration();
        }

        if (_modelDownloadService.DeleteModel(filename))
        {
            return Ok(new { Message = "Model deleted" });
        }

        return NotFound("Model not found");
    }

    /// <summary>
    /// Sets a local model as the active model and loads it.
    /// </summary>
    /// <param name="filename">The filename to set as active.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the operation.</returns>
    [HttpPost("Models/SetActive/{**filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetActiveModel(
        [FromRoute, Required] string filename,
        CancellationToken cancellationToken)
    {
        var localModels = _modelDownloadService.GetLocalModels();
        var model = localModels.FirstOrDefault(m => m.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase));

        if (model == null)
        {
            return NotFound("Model not found");
        }

        var config = Plugin.Instance?.Configuration;
        if (config != null)
        {
            // Unload current model first
            _llmService.UnloadModel();

            config.ModelPath = model.FullPath;
            config.ModelName = model.DisplayName;
            Plugin.Instance?.SaveConfiguration();

            // Load the new model so it's ready to use
            try
            {
                await _llmService.LoadModelAsync(model.FullPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load model after setting active");
                return Ok(new { Message = "Model set as active but failed to load: " + ex.Message, Path = model.FullPath });
            }
        }

        return Ok(new { Message = "Model set as active and loaded", Path = model.FullPath });
    }

    #endregion

    #region Native Libraries

    /// <summary>
    /// Gets the native library status.
    /// </summary>
    /// <returns>Native library status.</returns>
    [HttpGet("Native/Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<NativeLibraryStatus> GetNativeLibraryStatus()
    {
        return Ok(_modelDownloadService.GetNativeLibraryStatus());
    }

    /// <summary>
    /// Starts downloading native libraries for the current platform.
    /// </summary>
    /// <param name="cuda">Set to true to download CUDA (GPU) libraries instead of CPU.</param>
    /// <returns>Result indicating download started.</returns>
    [HttpPost("Native/Download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult StartNativeLibraryDownload([FromQuery] bool cuda = false)
    {
        if (_modelDownloadService.IsDownloading)
        {
            return Conflict(new { Message = "A download is already in progress" });
        }

        var started = _modelDownloadService.StartNativeLibraryDownload(useCuda: cuda);

        if (started)
        {
            var backend = cuda ? "CUDA (GPU)" : "CPU";
            return Ok(new { Message = $"Native library download started ({backend})" });
        }

        return BadRequest(new { Message = "Failed to start download" });
    }

    #endregion

    #region Rename Operations

    /// <summary>
    /// Generates rename suggestions for a specific item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rename suggestion.</returns>
    [HttpGet("Preview/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RenameSuggestionResponse>> PreviewRename(
        [FromRoute, Required] Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound("Item not found");
        }

        if (!_llmService.IsModelLoaded)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrEmpty(config.ModelPath))
            {
                return BadRequest("Model path is not configured");
            }
            await _llmService.LoadModelAsync(config.ModelPath, cancellationToken);
        }

        var suggestions = await _renamerService.GenerateRenameSuggestionsAsync(
            new[] { item },
            cancellationToken);

        if (suggestions.Count == 0)
        {
            return Ok(new RenameSuggestionResponse
            {
                ItemId = itemId,
                OriginalPath = item.Path ?? string.Empty,
                SuggestedPath = string.Empty,
                Message = "No rename suggestion generated"
            });
        }

        var suggestion = suggestions[0];
        return Ok(new RenameSuggestionResponse
        {
            ItemId = itemId,
            OriginalPath = suggestion.OriginalPath,
            SuggestedPath = suggestion.NewPath,
            Message = suggestion.Reason
        });
    }

    /// <summary>
    /// Executes a rename for a specific item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the rename operation.</returns>
    [HttpPost("Rename/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RenameItem(
        [FromRoute, Required] Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound("Item not found");
        }

        if (!_llmService.IsModelLoaded)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrEmpty(config.ModelPath))
            {
                return BadRequest("Model path is not configured");
            }
            await _llmService.LoadModelAsync(config.ModelPath, cancellationToken);
        }

        var suggestions = await _renamerService.GenerateRenameSuggestionsAsync(
            new[] { item },
            cancellationToken);

        if (suggestions.Count == 0)
        {
            return BadRequest("No rename suggestion generated");
        }

        var count = await _renamerService.ExecuteRenamesAsync(suggestions, cancellationToken);

        return Ok(new { Message = $"Renamed {count} file(s)", Suggestions = suggestions });
    }

    /// <summary>
    /// Tests the LLM with a sample filename.
    /// </summary>
    /// <param name="request">Test request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Test result.</returns>
    [HttpPost("Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TestResponse>> TestLlm(
        [FromBody] TestRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Filename))
        {
            return BadRequest("Filename is required");
        }

        if (!_llmService.IsModelLoaded)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrEmpty(config.ModelPath))
            {
                return BadRequest("Model path is not configured");
            }
            await _llmService.LoadModelAsync(config.ModelPath, cancellationToken);
        }

        var prompt = $"""
            Clean up a media filename for Jellyfin. Remove quality tags, release groups, and dots. Keep the extension.

            Examples:
            INPUT: The.Dark.Knight.2008.1080p.BluRay.x264-GROUP.mkv
            OUTPUT: The Dark Knight (2008).mkv
            INPUT: Breaking.Bad.S02E03.720p.HDTV.x264-LOL.mkv
            OUTPUT: Breaking Bad S02E03.mkv
            INPUT: [SubGroup] Attack on Titan - 05 (BD 1080p).mkv
            OUTPUT: Attack on Titan S01E05.mkv
            INPUT: interstellar.2014.brrip.mp4
            OUTPUT: Interstellar (2014).mp4

            INPUT: {request.Filename}
            OUTPUT:
            """;

        var result = await _llmService.GenerateAsync(prompt, cancellationToken);

        return Ok(new TestResponse
        {
            OriginalFilename = request.Filename,
            SuggestedFilename = result.Trim()
        });
    }

    #endregion
}

#region DTOs

/// <summary>
/// Model status response.
/// </summary>
public class ModelStatusResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the model is loaded.
    /// </summary>
    public bool IsModelLoaded { get; set; }

    /// <summary>
    /// Gets or sets the model path.
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model name.
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the models directory.
    /// </summary>
    public string ModelsDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a download is in progress.
    /// </summary>
    public bool IsDownloading { get; set; }

    /// <summary>
    /// Gets or sets the current download progress.
    /// </summary>
    public DownloadProgress? DownloadProgress { get; set; }
}

/// <summary>
/// Rename suggestion response.
/// </summary>
public class RenameSuggestionResponse
{
    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the original path.
    /// </summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the suggested new path.
    /// </summary>
    public string SuggestedPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Test request.
/// </summary>
public class TestRequest
{
    /// <summary>
    /// Gets or sets the filename to test.
    /// </summary>
    public string Filename { get; set; } = string.Empty;
}

/// <summary>
/// Test response.
/// </summary>
public class TestResponse
{
    /// <summary>
    /// Gets or sets the original filename.
    /// </summary>
    public string OriginalFilename { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the suggested filename.
    /// </summary>
    public string SuggestedFilename { get; set; } = string.Empty;
}

/// <summary>
/// Custom model download request.
/// </summary>
public class CustomModelDownloadRequest
{
    /// <summary>
    /// Gets or sets the URL to download from.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the filename to save as.
    /// </summary>
    public string Filename { get; set; } = string.Empty;
}

/// <summary>
/// Download progress response.
/// </summary>
public class DownloadProgressResponse
{
    /// <summary>
    /// Gets or sets whether a download is active.
    /// </summary>
    public bool IsDownloading { get; set; }

    /// <summary>
    /// Gets or sets the model ID.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the downloaded bytes.
    /// </summary>
    public long DownloadedBytes { get; set; }

    /// <summary>
    /// Gets or sets the total bytes.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the state (Starting, Downloading, Completed, Failed, Cancelled).
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the percentage (0-100).
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// Gets or sets the estimated seconds remaining.
    /// </summary>
    public double? EstimatedSecondsRemaining { get; set; }

    /// <summary>
    /// Gets or sets the completed path (only set when State is Completed).
    /// </summary>
    public string? CompletedPath { get; set; }
}

#endregion
