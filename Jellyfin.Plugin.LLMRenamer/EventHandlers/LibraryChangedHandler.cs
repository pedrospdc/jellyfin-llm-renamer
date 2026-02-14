using Jellyfin.Plugin.LLMRenamer.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LLMRenamer.EventHandlers;

/// <summary>
/// Handles library item added events for auto-renaming.
/// </summary>
public class LibraryChangedHandler : IHostedService, IDisposable
{
    private readonly ILogger<LibraryChangedHandler> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly ILlmService _llmService;
    private readonly FileRenamerService _renamerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangedHandler"/> class.
    /// </summary>
    public LibraryChangedHandler(
        ILogger<LibraryChangedHandler> logger,
        ILibraryManager libraryManager,
        ILlmService llmService,
        FileRenamerService renamerService)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _llmService = llmService;
        _renamerService = renamerService;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _logger.LogInformation("LLM Renamer library event handler initialized");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;

        // Check if auto-rename is enabled and not in preview mode
        if (config == null || !config.EnableAutoRename || config.PreviewOnly)
        {
            return;
        }

        // Check if this item type should be renamed
        var shouldProcess = e.Item switch
        {
            Movie when config.RenameMovies => true,
            Episode when config.RenameEpisodes => true,
            Audio when config.RenameMusic => true,
            _ => false
        };

        if (!shouldProcess)
        {
            return;
        }

        _logger.LogDebug("Processing newly added item: {ItemName}", e.Item.Name);

        try
        {
            // Ensure model is loaded
            if (!_llmService.IsModelLoaded)
            {
                if (string.IsNullOrEmpty(config.ModelPath))
                {
                    _logger.LogWarning("Model path not configured, skipping auto-rename");
                    return;
                }

                await _llmService.LoadModelAsync(config.ModelPath);
            }

            var suggestions = await _renamerService.GenerateRenameSuggestionsAsync(
                new[] { e.Item },
                CancellationToken.None);

            if (suggestions.Count > 0)
            {
                _logger.LogInformation(
                    "Auto-renaming: {Original} -> {New}",
                    Path.GetFileName(suggestions[0].OriginalPath),
                    Path.GetFileName(suggestions[0].NewPath));

                await _renamerService.ExecuteRenamesAsync(suggestions, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto-rename for {ItemName}", e.Item.Name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        GC.SuppressFinalize(this);
    }
}
