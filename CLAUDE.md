# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build the plugin
cd Jellyfin.Plugin.LLMRenamer
dotnet build -c Release

# Publish with all dependencies (for release packaging)
dotnet publish -c Release --no-restore -o publish

# Create release zip (after publish, exclude native runtimes)
cd publish && rm -rf runtimes && zip -r ../release.zip .
```

The plugin targets .NET 9.0 and Jellyfin 10.11+.

## Architecture

This is a Jellyfin plugin that uses LLamaSharp to run local LLM inference for renaming media files.

### Core Components

**Plugin.cs** - Entry point implementing `BasePlugin<PluginConfiguration>` and `IHasWebPages`. Exposes configuration page in admin UI via `EnableInMainMenu = true` with `MenuSection = "admin"`.

**PluginServiceRegistrator.cs** - DI registration via `IPluginServiceRegistrator`. Registers:
- `LlamaSharpService` as singleton `ILlmService`
- `FileRenamerService` for rename logic
- `ModelDownloadService` for downloading GGUF models
- `LibraryChangedHandler` as `IHostedService` for auto-rename on library changes

### Service Layer

**LlamaSharpService** - Wraps LLamaSharp for model loading/inference. Uses `StatelessExecutor` with `DefaultSamplingPipeline`. Requires native llama.cpp libraries configured via `NativeLibraryConfig.All.WithLibrary()`.

**FileRenamerService** - Builds prompts for movies/episodes/music with Jellyfin naming conventions, sends to LLM, cleans output. Pattern matches on `Movie`, `Episode`, `Audio` types.

**ModelDownloadService** - Downloads GGUF models from Hugging Face URLs with progress tracking. Also handles native library downloads from llama.cpp releases.

### API

**LLMRenamerController** - REST endpoints under `/LLMRenamer/` for model management, status, and rename operations. Requires admin authorization.

### Event Handling

**LibraryChangedHandler** - `IHostedService` that hooks into `ILibraryManager.ItemAdded` to auto-rename new media (when enabled).

**RenameFilesTask** - `IScheduledTask` for manual/scheduled batch renaming.

## Release Workflow

The GitHub Actions release workflow (`.github/workflows/release.yml`) automatically:
1. Extracts version from git tag (v0.0.7 → 0.0.7.0)
2. Updates `plugin.json` version BEFORE building
3. Publishes with `dotnet publish`
4. Removes `runtimes/` folder (prevents Jellyfin from loading native libs as assemblies)
5. Updates `manifest.json` with new version, checksum, URL
6. Commits and pushes the manifest update to main
7. Creates GitHub release

To create a release: `git tag v0.0.X && git push origin v0.0.X`

**Important:** The workflow auto-updates `manifest.json`. After creating a release, pull before making further commits:
```bash
git pull origin main
```

## Manual Manifest Update

If needed, manually update `manifest.json` with:
```bash
# Calculate checksum
md5sum jellyfin-llm-renamer-v0.0.X.zip

# Update manifest.json versions array with:
# - version: "0.0.X.0"
# - sourceUrl: GitHub release download URL
# - checksum: MD5 from above
# - timestamp: ISO 8601 format (e.g., 2026-02-14T04:00:00Z)
```

The manifest should contain only ONE version entry (the latest) to avoid Jellyfin repository errors.

## Key Files

- `plugin.json` - Plugin metadata (guid, version, targetAbi)
- `manifest.json` - Jellyfin plugin repository manifest
- `Configuration/configPage.html` - Embedded admin UI (HTML/JS)
- `Configuration/PluginConfiguration.cs` - User settings schema
