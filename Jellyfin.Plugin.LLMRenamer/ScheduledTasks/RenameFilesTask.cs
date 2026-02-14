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
            return;
        }

        if (string.IsNullOrEmpty(config.ModelPath))
        {
            _logger.LogError("Model path is not configured");
            return;
        }

        progress.Report(0);

        // Load the model if not already loaded
        if (!_llmService.IsModelLoaded)
        {
            _logger.LogInformation("Loading LLM model from {ModelPath}", config.ModelPath);
            await _llmService.LoadModelAsync(config.ModelPath, cancellationToken);
        }

        progress.Report(10);

        // Get all items that need renaming
        var items = GetItemsToRename(config);
        var totalItems = items.Count;

        if (totalItems == 0)
        {
            _logger.LogInformation("No items found to rename");
            progress.Report(100);
            return;
        }

        _logger.LogInformation("Found {Count} items to process", totalItems);

        // Generate rename suggestions
        var suggestions = await _renamerService.GenerateRenameSuggestionsAsync(items, cancellationToken);

        progress.Report(70);

        _logger.LogInformation("Generated {Count} rename suggestions", suggestions.Count);

        // Log suggestions
        foreach (var suggestion in suggestions)
        {
            _logger.LogInformation(
                "Suggestion: {Original} -> {New} ({Reason})",
                Path.GetFileName(suggestion.OriginalPath),
                Path.GetFileName(suggestion.NewPath),
                suggestion.Reason);
        }

        // Execute renames if not in preview mode
        if (!config.PreviewOnly && suggestions.Count > 0)
        {
            var renamed = await _renamerService.ExecuteRenamesAsync(suggestions, cancellationToken);
            _logger.LogInformation("Successfully renamed {Count} files", renamed);
        }
        else if (config.PreviewOnly)
        {
            _logger.LogInformation("Preview mode enabled - no files were renamed");
        }

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
        // No default triggers - user should manually run or configure
        return Array.Empty<TaskTriggerInfo>();
    }
}
