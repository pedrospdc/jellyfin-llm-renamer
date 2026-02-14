using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.LLMRenamer.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LLMRenamer.ScheduledTasks;

/// <summary>
/// Scheduled task for renaming files using LLM.
/// </summary>
public class RenameFilesTask : IScheduledTask
{
    private readonly ILogger<RenameFilesTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly ILlmService _llmService;
    private readonly FileRenamerService _renamerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenameFilesTask"/> class.
    /// </summary>
    public RenameFilesTask(
        ILogger<RenameFilesTask> logger,
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
    public string Name => "LLM File Renamer";

    /// <inheritdoc />
    public string Key => "LLMFileRenamer";

    /// <inheritdoc />
    public string Description => "Renames media files using AI to follow Jellyfin naming conventions.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;

        if (config == null)
        {
            _logger.LogError("Plugin configuration is null");
            PluginLog.Error("Plugin configuration is null");
            return;
        }

        if (string.IsNullOrEmpty(config.ModelPath))
        {
            _logger.LogError("Model path is not configured");
            PluginLog.Error("Scheduled task aborted: model path is not configured");
            return;
        }

        PluginLog.Info("=== Scheduled rename task started ===");
        progress.Report(0);

        // Load the model if not already loaded
        if (!_llmService.IsModelLoaded)
        {
            _logger.LogInformation("Loading LLM model from {ModelPath}", config.ModelPath);
            try
            {
                await _llmService.LoadModelAsync(config.ModelPath, cancellationToken);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to load model: {ex.Message}");
                throw;
            }
        }

        progress.Report(10);

        // Get all items that need renaming
        var items = GetItemsToRename(config);
        var totalItems = items.Count;

        if (totalItems == 0)
        {
            _logger.LogInformation("No items found to rename");
            PluginLog.Info("No items found to rename");
            progress.Report(100);
            return;
        }

        _logger.LogInformation("Found {Count} items to process", totalItems);
        PluginLog.Info($"Found {totalItems} items to process (movies={config.RenameMovies}, episodes={config.RenameEpisodes}, music={config.RenameMusic})");

        // Generate rename suggestions one item at a time for progress tracking
        var suggestions = new List<FileRenamerService.RenameOperation>();
        var skippedCount = 0;
        for (var i = 0; i < totalItems; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemSuggestions = await _renamerService.GenerateRenameSuggestionsAsync(
                new[] { items[i] }, cancellationToken);
            suggestions.AddRange(itemSuggestions);

            // Report progress: 10-80% for generation phase
            var pct = 10 + (70.0 * (i + 1) / totalItems);
            progress.Report(pct);

            if (itemSuggestions.Count > 0)
            {
                foreach (var s in itemSuggestions)
                {
                    var msg = $"Suggestion ({i + 1}/{totalItems}): {Path.GetFileName(s.OriginalPath)} -> {Path.GetFileName(s.NewPath)}";
                    _logger.LogInformation(
                        "Suggestion ({Index}/{Total}): {Original} -> {New} ({Reason})",
                        i + 1, totalItems,
                        Path.GetFileName(s.OriginalPath),
                        Path.GetFileName(s.NewPath),
                        s.Reason);
                    PluginLog.Info(msg);
                }
            }
            else
            {
                skippedCount++;
            }
        }

        _logger.LogInformation("Generated {Count} rename suggestions from {Total} items", suggestions.Count, totalItems);
        PluginLog.Info($"Generation complete: {suggestions.Count} suggestions, {skippedCount} skipped (already correct)");

        // Execute renames if not in preview mode
        if (!config.PreviewOnly && suggestions.Count > 0)
        {
            var renamed = await _renamerService.ExecuteRenamesAsync(suggestions, cancellationToken);
            _logger.LogInformation("Successfully renamed {Count} files", renamed);
            PluginLog.Info($"Renamed {renamed} files/directories");
        }
        else if (config.PreviewOnly)
        {
            _logger.LogInformation("Preview mode enabled - no files were renamed");
            PluginLog.Info("Preview mode - no files were renamed");
        }

        // Unload model to free memory after batch processing
        _llmService.UnloadModel();

        PluginLog.Info("=== Scheduled rename task completed ===");
        progress.Report(100);
    }

    private List<BaseItem> GetItemsToRename(Configuration.PluginConfiguration config)
    {
        var items = new List<BaseItem>();

        var query = new InternalItemsQuery
        {
            IsVirtualItem = false,
            Recursive = true,
        };

        if (config.RenameMovies)
        {
            query.IncludeItemTypes = new[] { BaseItemKind.Movie };
            items.AddRange(_libraryManager.GetItemList(query));
        }

        if (config.RenameEpisodes)
        {
            query.IncludeItemTypes = new[] { BaseItemKind.Episode };
            items.AddRange(_libraryManager.GetItemList(query));
        }

        if (config.RenameMusic)
        {
            query.IncludeItemTypes = new[] { BaseItemKind.Audio };
            items.AddRange(_libraryManager.GetItemList(query));
        }

        return items;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(24).Ticks
            }
        };
    }
}
