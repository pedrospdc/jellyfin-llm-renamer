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

**LlamaSharpService** - Wraps LLamaSharp for model loading/inference. Uses `StatelessExecutor` with `DefaultSamplingPipeline`. Native libraries are discovered via `NativeLibraryConfig.All.WithSearchDirectory()` pointing to the plugin data directory.

**FileRenamerService** - Builds prompts for movies/episodes/music with Jellyfin naming conventions, sends to LLM, cleans output. Pattern matches on `Movie`, `Episode`, `Audio` types.

**ModelDownloadService** - Downloads GGUF models from Hugging Face URLs with progress tracking. Also downloads native libraries from the LLamaSharp.Backend.Cpu NuGet package, extracting them into `runtimes/{rid}/native/{avx}/` structure in the plugin data directory.

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

## Testing

### Deploy and Test Locally

Jellyfin runs on the Windows host (accessible from WSL at `172.29.32.1:8096`). API key: `b7dcc95167a64c96b3204b4ab6758dbd`.

```bash
API_KEY="b7dcc95167a64c96b3204b4ab6758dbd"
HOST="http://172.29.32.1:8096"
PLUGIN_DIR="/mnt/c/ProgramData/Jellyfin/Server/plugins/LLMRenamer_0.0.18.0"
PUBLISH_DIR="/mnt/g/Torrents/jellyfin-llm-renamer/Jellyfin.Plugin.LLMRenamer/publish"

# 1. Build
cd /mnt/g/Torrents/jellyfin-llm-renamer/Jellyfin.Plugin.LLMRenamer
dotnet publish -c Release --no-restore -o publish

# 2. Shutdown Jellyfin
curl -s -X POST "$HOST/System/Shutdown" -H "X-Emby-Token: $API_KEY"

# 3. Wait, then deploy
sleep 10
cp "$PUBLISH_DIR/Jellyfin.Plugin.LLMRenamer.dll" "$PLUGIN_DIR/"

# 4. Restart Jellyfin
curl -s -X POST "$HOST/System/Restart" -H "X-Emby-Token: $API_KEY"

# 5. Wait for Jellyfin to start, then check status
sleep 30
curl -s "$HOST/LLMRenamer/Status" -H "X-Emby-Token: $API_KEY"
```

### Key API Endpoints

```bash
# Plugin status
curl -s "$HOST/LLMRenamer/Status" -H "X-Emby-Token: $API_KEY"

# Download native libraries (from LLamaSharp NuGet package)
curl -s -X POST "$HOST/LLMRenamer/Native/Download" -H "X-Emby-Token: $API_KEY"

# Download a model
curl -s -X POST "$HOST/LLMRenamer/Models/Download/qwen2.5-0.5b-instruct-q4_k_m" -H "X-Emby-Token: $API_KEY"

# Check download progress
curl -s "$HOST/LLMRenamer/Models/DownloadProgress" -H "X-Emby-Token: $API_KEY"

# Load/activate a model
curl -s -X POST "$HOST/LLMRenamer/Models/SetActive/qwen2.5-0.5b-instruct-q4_k_m.gguf" -H "X-Emby-Token: $API_KEY"

# Test LLM rename
curl -s -X POST "$HOST/LLMRenamer/Test" -H "X-Emby-Token: $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"Filename":"The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv"}'

# Native library status
curl -s "$HOST/LLMRenamer/Native/Status" -H "X-Emby-Token: $API_KEY"
```

### Important: Native Library Architecture

Native llama.cpp libraries **cannot** live in the plugin install directory (`LLMRenamer_0.0.18.0/`). Jellyfin recursively scans all subdirectories under `plugins/` and tries to load every DLL as a .NET assembly. Native DLLs cause `BadImageFormatException` and the plugin gets disabled.

Instead, native libs are downloaded to the plugin **data** directory (`Jellyfin.Plugin.LLMRenamer/runtimes/{rid}/native/{avx}/`). Jellyfin still scans these and logs errors, but since it's a separate directory from the install folder, the actual plugin remains loaded.

LLamaSharp finds them via `NativeLibraryConfig.All.WithSearchDirectory(dataDir)` which searches for `runtimes/{rid}/native/` subdirectories.

## Key Files

- `plugin.json` - Plugin metadata (guid, version, targetAbi)
- `manifest.json` - Jellyfin plugin repository manifest
- `Configuration/configPage.html` - Embedded admin UI (HTML/JS)
- `Configuration/PluginConfiguration.cs` - User settings schema
