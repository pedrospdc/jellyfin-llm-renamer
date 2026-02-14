using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LLMRenamer.Services;

/// <summary>
/// Service for renaming media files using LLM.
/// </summary>
public partial class FileRenamerService
{
    private readonly ILogger<FileRenamerService> _logger;
    private readonly ILlmService _llmService;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileRenamerService"/> class.
    /// </summary>
    public FileRenamerService(
        ILogger<FileRenamerService> logger,
        ILlmService llmService,
        ILibraryManager libraryManager)
    {
        _logger = logger;
        _llmService = llmService;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Represents a rename operation.
    /// </summary>
    public record RenameOperation(string OriginalPath, string NewPath, string Reason, bool IsDirectory = false);

    /// <summary>
    /// Generates rename suggestions for items in a library.
    /// </summary>
    public async Task<List<RenameOperation>> GenerateRenameSuggestionsAsync(
        IEnumerable<BaseItem> items,
        CancellationToken cancellationToken = default)
    {
        var operations = new List<RenameOperation>();
        var config = Plugin.Instance?.Configuration;

        if (config == null)
        {
            _logger.LogWarning("Plugin configuration is null");
            return operations;
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var suggestion = item switch
                {
                    Movie movie when config.RenameMovies => await GenerateMovieRenameAsync(movie, cancellationToken),
                    Episode episode when config.RenameEpisodes => await GenerateEpisodeRenameAsync(episode, cancellationToken),
                    Audio audio when config.RenameMusic => await GenerateMusicRenameAsync(audio, cancellationToken),
                    _ => null
                };

                if (suggestion != null)
                {
                    operations.Add(suggestion);
                }

                // Generate directory renames if enabled
                if (config.RenameDirectories)
                {
                    var dirRenames = item switch
                    {
                        Movie movie when config.RenameMovies => GenerateMovieDirectoryRenames(movie),
                        Episode episode when config.RenameEpisodes => GenerateEpisodeDirectoryRenames(episode),
                        _ => Enumerable.Empty<RenameOperation>()
                    };

                    foreach (var dirRename in dirRenames)
                    {
                        // Deduplicate: don't add if we already have a rename for the same original path
                        if (!operations.Any(o => o.OriginalPath == dirRename.OriginalPath))
                        {
                            operations.Add(dirRename);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating rename suggestion for {ItemName}", item.Name);
            }
        }

        return operations;
    }

    private async Task<RenameOperation?> GenerateMovieRenameAsync(Movie movie, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(movie.Path))
        {
            return null;
        }

        var originalFileName = Path.GetFileName(movie.Path);
        var extension = Path.GetExtension(movie.Path);
        var directory = Path.GetDirectoryName(movie.Path) ?? string.Empty;

        var prompt = BuildMoviePrompt(originalFileName, movie);

        _logger.LogDebug("Generating rename for movie: {OriginalName}", originalFileName);

        var suggestion = await _llmService.GenerateAsync(prompt, cancellationToken);
        var newFileName = CleanLlmOutput(suggestion, extension);

        if (string.IsNullOrEmpty(newFileName) || newFileName == originalFileName)
        {
            return null;
        }

        var newPath = Path.Combine(directory, newFileName);
        return new RenameOperation(movie.Path, newPath, $"Movie: {movie.Name} ({movie.ProductionYear})");
    }

    private async Task<RenameOperation?> GenerateEpisodeRenameAsync(Episode episode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(episode.Path))
        {
            return null;
        }

        var originalFileName = Path.GetFileName(episode.Path);
        var extension = Path.GetExtension(episode.Path);
        var directory = Path.GetDirectoryName(episode.Path) ?? string.Empty;

        var prompt = BuildEpisodePrompt(originalFileName, episode);

        _logger.LogDebug("Generating rename for episode: {OriginalName}", originalFileName);

        var suggestion = await _llmService.GenerateAsync(prompt, cancellationToken);
        var newFileName = CleanLlmOutput(suggestion, extension);

        if (string.IsNullOrEmpty(newFileName) || newFileName == originalFileName)
        {
            return null;
        }

        var newPath = Path.Combine(directory, newFileName);
        return new RenameOperation(
            episode.Path,
            newPath,
            $"Episode: {episode.SeriesName} S{episode.ParentIndexNumber:D2}E{episode.IndexNumber:D2}");
    }

    private async Task<RenameOperation?> GenerateMusicRenameAsync(Audio audio, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(audio.Path))
        {
            return null;
        }

        var originalFileName = Path.GetFileName(audio.Path);
        var extension = Path.GetExtension(audio.Path);
        var directory = Path.GetDirectoryName(audio.Path) ?? string.Empty;

        var prompt = BuildMusicPrompt(originalFileName, audio);

        _logger.LogDebug("Generating rename for music: {OriginalName}", originalFileName);

        var suggestion = await _llmService.GenerateAsync(prompt, cancellationToken);
        var newFileName = CleanLlmOutput(suggestion, extension);

        if (string.IsNullOrEmpty(newFileName) || newFileName == originalFileName)
        {
            return null;
        }

        var newPath = Path.Combine(directory, newFileName);
        return new RenameOperation(audio.Path, newPath, $"Track: {audio.Album} - {audio.Name}");
    }

    private IEnumerable<RenameOperation> GenerateMovieDirectoryRenames(Movie movie)
    {
        if (string.IsNullOrEmpty(movie.Path))
        {
            yield break;
        }

        var dir = Path.GetDirectoryName(movie.Path);
        if (string.IsNullOrEmpty(dir))
        {
            yield break;
        }

        var dirName = Path.GetFileName(dir);
        var parentDir = Path.GetDirectoryName(dir);
        if (string.IsNullOrEmpty(parentDir) || string.IsNullOrEmpty(dirName))
        {
            yield break;
        }

        var year = movie.ProductionYear;
        var newDirName = year.HasValue
            ? $"{movie.Name} ({year})"
            : movie.Name;

        newDirName = SanitizeDirectoryName(newDirName);

        if (dirName == newDirName)
        {
            yield break;
        }

        var newPath = Path.Combine(parentDir, newDirName);
        yield return new RenameOperation(dir, newPath, $"Movie directory: {movie.Name}", IsDirectory: true);
    }

    private IEnumerable<RenameOperation> GenerateEpisodeDirectoryRenames(Episode episode)
    {
        if (string.IsNullOrEmpty(episode.Path))
        {
            yield break;
        }

        // Season directory (immediate parent of episode file)
        var seasonDir = Path.GetDirectoryName(episode.Path);
        if (string.IsNullOrEmpty(seasonDir))
        {
            yield break;
        }

        var seasonDirName = Path.GetFileName(seasonDir);
        var seriesDir = Path.GetDirectoryName(seasonDir);
        if (string.IsNullOrEmpty(seriesDir) || string.IsNullOrEmpty(seasonDirName))
        {
            yield break;
        }

        // Rename season directory to "Season XX"
        if (episode.ParentIndexNumber.HasValue)
        {
            var newSeasonDirName = $"Season {episode.ParentIndexNumber.Value:D2}";
            if (seasonDirName != newSeasonDirName)
            {
                var newSeasonPath = Path.Combine(seriesDir, newSeasonDirName);
                yield return new RenameOperation(seasonDir, newSeasonPath, $"Season directory: {newSeasonDirName}", IsDirectory: true);
            }
        }

        // Series directory (grandparent of episode file)
        var seriesDirName = Path.GetFileName(seriesDir);
        var seriesParentDir = Path.GetDirectoryName(seriesDir);
        if (string.IsNullOrEmpty(seriesParentDir) || string.IsNullOrEmpty(seriesDirName))
        {
            yield break;
        }

        if (!string.IsNullOrEmpty(episode.SeriesName))
        {
            var seriesYear = episode.Series?.PremiereDate?.Year;
            var newSeriesDirName = seriesYear.HasValue
                ? $"{episode.SeriesName} ({seriesYear})"
                : episode.SeriesName;

            newSeriesDirName = SanitizeDirectoryName(newSeriesDirName);

            if (seriesDirName != newSeriesDirName)
            {
                var newSeriesPath = Path.Combine(seriesParentDir, newSeriesDirName);
                yield return new RenameOperation(seriesDir, newSeriesPath, $"Series directory: {episode.SeriesName}", IsDirectory: true);
            }
        }
    }

    private static string SanitizeDirectoryName(string name)
    {
        // Remove characters invalid in directory names
        return InvalidCharsRegex().Replace(name, "_").Trim();
    }

    private static string BuildMoviePrompt(string originalFileName, Movie movie)
    {
        var customAdditions = Plugin.Instance?.Configuration?.CustomPromptAdditions ?? string.Empty;

        return $"""
            You are a file naming assistant. Rename the following movie file to follow Jellyfin naming conventions.

            JELLYFIN MOVIE NAMING RULES:
            - Format: "Movie Title (Year).ext"
            - Use the actual release year in parentheses
            - Remove quality tags, release groups, and extra info from the filename
            - Keep the original file extension
            - For versions, use: "Movie Title (Year) - 1080p.ext" or "Movie Title (Year) - [Directors Cut].ext"
            - Replace special characters that aren't filesystem-safe

            ORIGINAL FILENAME: {originalFileName}

            METADATA (if available):
            - Title: {movie.Name ?? "Unknown"}
            - Year: {movie.ProductionYear?.ToString() ?? "Unknown"}
            - IMDB: {GetProviderId(movie, MetadataProvider.Imdb)}
            - TMDB: {GetProviderId(movie, MetadataProvider.Tmdb)}

            {customAdditions}

            Respond with ONLY the new filename (including extension). No explanation.
            NEW FILENAME:
            """;
    }

    private static string BuildEpisodePrompt(string originalFileName, Episode episode)
    {
        var customAdditions = Plugin.Instance?.Configuration?.CustomPromptAdditions ?? string.Empty;

        return $"""
            You are a file naming assistant. Rename the following TV episode file to follow Jellyfin naming conventions.

            JELLYFIN TV EPISODE NAMING RULES:
            - Format: "Series Name S##E## - Episode Title.ext"
            - Season number: two digits with leading zero (S01, S02, etc.)
            - Episode number: two digits with leading zero (E01, E02, etc.)
            - Multi-episode: "S01E01-E02.ext"
            - Specials go in Season 00: "S00E01.ext"
            - Remove quality tags, release groups, and extra info
            - Keep the original file extension

            ORIGINAL FILENAME: {originalFileName}

            METADATA (if available):
            - Series: {episode.SeriesName ?? "Unknown"}
            - Season: {episode.ParentIndexNumber?.ToString() ?? "Unknown"}
            - Episode: {episode.IndexNumber?.ToString() ?? "Unknown"}
            - Episode Title: {episode.Name ?? "Unknown"}

            {customAdditions}

            Respond with ONLY the new filename (including extension). No explanation.
            NEW FILENAME:
            """;
    }

    private static string BuildMusicPrompt(string originalFileName, Audio audio)
    {
        var customAdditions = Plugin.Instance?.Configuration?.CustomPromptAdditions ?? string.Empty;

        return $"""
            You are a file naming assistant. Rename the following music file to follow Jellyfin naming conventions.

            JELLYFIN MUSIC NAMING RULES:
            - Format: "## - Track Title.ext" where ## is the track number with leading zero
            - Keep the original file extension
            - Remove quality tags and extra info from filename
            - Music metadata is primarily read from embedded tags, but clean filenames help

            ORIGINAL FILENAME: {originalFileName}

            METADATA (if available):
            - Track: {audio.IndexNumber?.ToString() ?? "Unknown"}
            - Title: {audio.Name ?? "Unknown"}
            - Album: {audio.Album ?? "Unknown"}
            - Artist: {string.Join(", ", audio.Artists ?? Array.Empty<string>())}

            {customAdditions}

            Respond with ONLY the new filename (including extension). No explanation.
            NEW FILENAME:
            """;
    }

    private string CleanLlmOutput(string output, string expectedExtension)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        // Remove common LLM artifacts
        var cleaned = output
            .Trim()
            .TrimStart('`')
            .TrimEnd('`')
            .Trim('"')
            .Trim();

        // Remove any "NEW FILENAME:" prefix if the model repeated it
        if (cleaned.StartsWith("NEW FILENAME:", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["NEW FILENAME:".Length..].Trim();
        }

        // Remove invalid filename characters
        cleaned = InvalidCharsRegex().Replace(cleaned, "_");

        // Ensure it has the correct extension
        if (!cleaned.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            // If it has a different extension, replace it
            var currentExt = Path.GetExtension(cleaned);
            if (!string.IsNullOrEmpty(currentExt))
            {
                cleaned = cleaned[..^currentExt.Length];
            }
            cleaned += expectedExtension;
        }

        return cleaned;
    }

    /// <summary>
    /// Executes the rename operations.
    /// </summary>
    public Task<int> ExecuteRenamesAsync(
        IEnumerable<RenameOperation> operations,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        var allOps = operations.ToList();

        // Process file renames first
        var fileOps = allOps.Where(o => !o.IsDirectory).ToList();
        foreach (var op in fileOps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(op.OriginalPath) && !File.Exists(op.NewPath))
                {
                    _logger.LogInformation("Renaming file: {Original} -> {New}", op.OriginalPath, op.NewPath);
                    File.Move(op.OriginalPath, op.NewPath);
                    count++;
                }
                else if (File.Exists(op.NewPath))
                {
                    _logger.LogWarning("Target file already exists, skipping: {NewPath}", op.NewPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename file {Original}", op.OriginalPath);
            }
        }

        // Process directory renames: deepest paths first so inner dirs are renamed before outer dirs
        var dirOps = allOps.Where(o => o.IsDirectory)
            .OrderByDescending(o => o.OriginalPath.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ToList();

        // Track renames so we can update subsequent paths when a parent directory is renamed
        var pathMappings = new List<(string OldPrefix, string NewPrefix)>();

        foreach (var op in dirOps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Apply any earlier path mappings to this operation
                var currentOriginal = ApplyPathMappings(op.OriginalPath, pathMappings);
                var currentNew = ApplyPathMappings(op.NewPath, pathMappings);

                if (Directory.Exists(currentOriginal) && !Directory.Exists(currentNew))
                {
                    _logger.LogInformation("Renaming directory: {Original} -> {New}", currentOriginal, currentNew);
                    Directory.Move(currentOriginal, currentNew);
                    pathMappings.Add((currentOriginal, currentNew));
                    count++;
                }
                else if (Directory.Exists(currentNew))
                {
                    _logger.LogWarning("Target directory already exists, skipping: {NewPath}", currentNew);
                }
                else if (!Directory.Exists(currentOriginal))
                {
                    _logger.LogWarning("Source directory does not exist, skipping: {OriginalPath}", currentOriginal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename directory {Original}", op.OriginalPath);
            }
        }

        // Trigger library refresh if any renames occurred
        if (count > 0)
        {
            _logger.LogInformation("Renamed {Count} files/directories, triggering library scan", count);
        }

        return Task.FromResult(count);
    }

    private static string ApplyPathMappings(string path, List<(string OldPrefix, string NewPrefix)> mappings)
    {
        foreach (var (oldPrefix, newPrefix) in mappings)
        {
            if (path.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = newPrefix + path[oldPrefix.Length..];
            }
        }

        return path;
    }

    private static string GetProviderId(BaseItem item, MetadataProvider provider)
    {
        if (item.ProviderIds.TryGetValue(provider.ToString(), out var id))
        {
            return id ?? "N/A";
        }
        return "N/A";
    }

    [GeneratedRegex(@"[<>:""/\\|?*]")]
    private static partial Regex InvalidCharsRegex();
}
