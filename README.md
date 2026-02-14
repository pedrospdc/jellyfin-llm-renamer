# Jellyfin LLM File Renamer

A Jellyfin plugin that uses local AI models (via LLamaSharp) to automatically rename media files following Jellyfin's naming conventions. No external services required - runs entirely locally.

## Features

- **GPU Acceleration** - NVIDIA CUDA 12 support for 10-15x faster inference
- **Built-in Model Downloader** - Download models directly from the admin UI
- **Directory Renaming** - Optionally rename parent directories to match Jellyfin conventions
- **Plugin Logging** - Dedicated log file (`llm-renamer.log`) for easy debugging
- Renames movies to `Movie Title (Year).ext` format
- Renames TV episodes to `Series Name S##E## - Episode Title.ext` format
- Renames music to `## - Track Title.ext` format
- Uses local LLM inference via LLamaSharp (no Ollama dependency)
- Supports Qwen, Llama, Phi, and other GGUF models
- Preview mode to see suggestions before renaming
- Auto-rename on library scan (optional)
- Test rename feature in the admin UI
- API endpoints for programmatic access
- Native library auto-download for Windows, Linux, and macOS

## Requirements

- Jellyfin 10.11.0 or later
- Sufficient RAM for model loading (3B model needs ~4GB)
- **For GPU acceleration (optional):** NVIDIA GPU + [CUDA 12 Toolkit](https://developer.nvidia.com/cuda-12-6-0-download-archive)

## Installation

### Option 1: Plugin Repository (Recommended)

Add this plugin repository to Jellyfin:

1. Go to **Dashboard > Plugins > Repositories**
2. Click **Add** and enter:
   - **Repository Name:** `LLM File Renamer`
   - **Repository URL:** `https://raw.githubusercontent.com/pedrospdc/jellyfin-llm-renamer/main/manifest.json`
3. Click **Save**
4. Go to **Catalog** tab
5. Find **LLM File Renamer** under Metadata and click **Install**
6. Restart Jellyfin

### Option 2: Manual Installation from Release

1. Download the latest release from [GitHub Releases](https://github.com/pedrospdc/jellyfin-llm-renamer/releases)
2. Extract the zip file
3. Copy all files to your Jellyfin plugins directory:
   - Linux: `/var/lib/jellyfin/plugins/LLMRenamer/`
   - Windows: `C:\ProgramData\Jellyfin\Server\plugins\LLMRenamer\`
   - Docker: `/config/plugins/LLMRenamer/`
4. Restart Jellyfin

### Option 3: Building from Source

```bash
git clone https://github.com/pedrospdc/jellyfin-llm-renamer.git
cd jellyfin-llm-renamer/Jellyfin.Plugin.LLMRenamer
dotnet publish -c Release --no-restore -o publish
```

The plugin files will be in `publish/`. Copy all DLLs and `plugin.json` to your Jellyfin plugins directory, then restart Jellyfin.

### First-Time Setup

1. Go to **Dashboard > Plugins > LLM File Renamer**
2. Click **Download Native Libraries** (required once per platform)
3. Download the recommended model (Qwen 2.5 3B)
4. Configure your preferences

## GPU Acceleration (Optional)

The plugin supports NVIDIA GPU acceleration via CUDA 12 for significantly faster inference (~200ms vs ~2-3s per item).

### Setup

1. Install the [CUDA 12 Toolkit](https://developer.nvidia.com/cuda-12-6-0-download-archive) (v12.x required, CUDA 13 is not compatible)
2. In the plugin config, click **Download Native Libraries** with the **CUDA** option selected
3. Set **GPU Layers** to a value > 0 (e.g., 33 for full offload of a 3B model)
4. Restart Jellyfin

### How It Works

- The plugin automatically detects CUDA 12 toolkit installations on Windows (via `CUDA_PATH` or standard install paths)
- Pre-loads CUDA runtime libraries (`cublas64_12.dll`, `cudart64_12.dll`) before loading the LLM backend
- Falls back to CPU automatically if CUDA loading fails
- Both CPU and CUDA native libraries can coexist - the plugin picks the right one based on your GPU Layers setting

### Notes

- CUDA 12.x is required - CUDA 13 libraries are not compatible with the current LLamaSharp backend
- If you have multiple CUDA versions installed, the plugin searches for v12.x specifically
- On Linux, ensure CUDA 12 libraries are in your library path

## Getting a Model

### Option 1: Download from Admin UI (Recommended)

1. Go to Dashboard > Plugins > LLM File Renamer
2. In the "Download Model" section, click **Download** next to your preferred model
3. Wait for the download to complete (progress is shown)
4. The model is automatically configured once downloaded

### Option 2: Download Custom Model

In the admin UI, you can also download any GGUF model by providing:
- Direct URL to the `.gguf` file
- Desired filename

### Option 3: Manual Download

Download a GGUF model from Hugging Face and specify the path manually.

## Available Models

| Model | Size | Description |
|-------|------|-------------|
| **Qwen 2.5 3B** (Recommended) | ~2GB | Best rename quality, reliable instruction following |
| Qwen 2.5 1.5B | ~1GB | Good balance of speed and quality |
| Qwen 2.5 0.5B | ~400MB | Fastest, lower rename quality |
| Llama 3.2 1B | ~800MB | Alternative 1B model |
| Phi-3 Mini 4K | ~2.3GB | Alternative 3.8B model |

## Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| Model Path | Full path to the GGUF model file | Auto-set when downloading |
| GPU Layers | Number of layers to offload to GPU (0 for CPU-only) | 0 |
| Context Size | Context window size | 2048 |
| Max Tokens | Maximum tokens to generate | 256 |
| Preview Only | Show suggestions without renaming | true |
| Enable Auto-Rename | Rename files on library scan | false |
| Rename Movies | Process movie files | true |
| Rename Episodes | Process TV episode files | true |
| Rename Music | Process music files | false |
| Rename Directories | Rename parent directories to Jellyfin conventions | false |

## Directory Renaming

When enabled, the plugin also renames parent directories using Jellyfin metadata (no LLM needed):

- **Movies:** `The.Matrix.1999.1080p/` → `The Matrix (1999)/`
- **TV Series:** `breaking.bad.complete/` → `Breaking Bad (2008)/`
- **TV Seasons:** `Season.1.720p/` → `Season 01/`

This uses metadata directly (title, year, season number) for reliable, deterministic renaming.

## Testing

The admin UI includes a test feature:

1. Enter a sample filename (e.g., `The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv`)
2. Click "Test Rename"
3. See the suggested new filename

## Jellyfin Naming Conventions

The plugin follows Jellyfin's official naming conventions:

### Movies
```
Movies/
├── Avatar (2009).mkv
├── The Matrix (1999)/
│   └── The Matrix (1999).mkv
└── Inception (2010) - 1080p.mkv
```

### TV Shows
```
TV Shows/
└── Breaking Bad (2008)/
    ├── Season 01/
    │   ├── Breaking Bad S01E01 - Pilot.mkv
    │   └── Breaking Bad S01E02 - Cat's in the Bag.mkv
    └── Season 02/
        └── Breaking Bad S02E01 - Seven Thirty-Seven.mkv
```

### Music
```
Music/
└── Artist Name/
    └── Album Name/
        ├── 01 - Track One.mp3
        └── 02 - Track Two.mp3
```

## API Endpoints

All endpoints require admin authentication via `X-Emby-Token` header.

### Status & Model Management

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/LLMRenamer/Status` | GET | Get model status and download progress |
| `/LLMRenamer/LoadModel` | POST | Load the configured model |
| `/LLMRenamer/UnloadModel` | POST | Unload the model from memory |
| `/LLMRenamer/Models/Available` | GET | List available models for download |
| `/LLMRenamer/Models/Local` | GET | List locally installed models |
| `/LLMRenamer/Models/Download/{modelId}` | POST | Download a predefined model |
| `/LLMRenamer/Models/DownloadCustom` | POST | Download from custom URL |
| `/LLMRenamer/Models/DownloadProgress` | GET | Get current download progress |
| `/LLMRenamer/Models/SetActive/{filename}` | POST | Set a model as active and load it |
| `/LLMRenamer/Models/{filename}` | DELETE | Delete a local model |
| `/LLMRenamer/Native/Status` | GET | Get native library status |
| `/LLMRenamer/Native/Download` | POST | Download native libraries for current platform |

### Rename Operations

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/LLMRenamer/Preview/{itemId}` | GET | Preview rename for an item |
| `/LLMRenamer/Rename/{itemId}` | POST | Execute rename for an item |
| `/LLMRenamer/Test` | POST | Test LLM with a sample filename |

### API Examples

**Download a model:**
```bash
curl -X POST "http://localhost:8096/LLMRenamer/Models/Download/qwen2.5-3b-instruct-q4_k_m" \
  -H "X-Emby-Token: YOUR_TOKEN"
```

**Test rename:**
```bash
curl -X POST "http://localhost:8096/LLMRenamer/Test" \
  -H "X-Emby-Token: YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"Filename": "The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv"}'
```

**Download custom model:**
```bash
curl -X POST "http://localhost:8096/LLMRenamer/Models/DownloadCustom" \
  -H "X-Emby-Token: YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"Url": "https://example.com/model.gguf", "Filename": "my-model.gguf"}'
```

## Scheduled Task

The plugin adds a scheduled task "LLM File Renamer" under Library tasks. You can:
- Run it manually from Dashboard > Scheduled Tasks
- Configure triggers for automatic execution

## Logging

The plugin writes to a dedicated log file `llm-renamer.log` in Jellyfin's log directory for easy debugging:
- Windows: `C:\ProgramData\Jellyfin\Server\log\llm-renamer.log`
- Linux: `/var/log/jellyfin/llm-renamer.log`
- Docker: `/config/log/llm-renamer.log`

This log captures model loading, CUDA detection, rename operations, and errors without cluttering the main Jellyfin log.

## Troubleshooting

### Native libraries not found
- Go to the plugin config page and click "Download Native Libraries"
- This downloads the correct llama.cpp binaries for your platform from the LLamaSharp NuGet package
- Libraries are stored in the plugin data directory under `runtimes/`

### Model fails to load
- Ensure native libraries are downloaded first
- Verify the model file is not corrupted (re-download if needed)
- Ensure you have enough RAM available
- Check Jellyfin logs for detailed error messages

### Download fails
- Check your internet connection
- Verify Jellyfin has write access to the plugin data directory
- Try downloading a smaller model first

### Poor rename suggestions
- Use the recommended Qwen 2.5 3B model (smaller models produce worse results)
- Add custom prompt instructions in configuration
- Ensure metadata is properly scraped for your media

### GPU not being used
- Ensure CUDA 12 toolkit is installed (not CUDA 13)
- Download native libraries with the CUDA option enabled
- Set GPU Layers > 0 in plugin configuration
- Check `llm-renamer.log` for CUDA detection messages
- Restart Jellyfin after installing CUDA toolkit (env vars need a fresh process)

### High memory usage
- The model stays loaded in memory between renames - unload it via the API (`POST /LLMRenamer/UnloadModel`) when not in use
- Use a smaller quantized model (Q4_K_M)
- Reduce context size to 1024
- With GPU acceleration, VRAM is used instead of system RAM for offloaded layers

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

MIT License
