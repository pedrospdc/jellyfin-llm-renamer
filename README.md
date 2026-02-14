# Jellyfin LLM File Renamer

A Jellyfin plugin that uses local AI models (via LLamaSharp) to automatically rename media files following Jellyfin's naming conventions. No external services required - runs entirely locally.

## Features

- **Built-in Model Downloader** - Download models directly from the admin UI
- Renames movies to `Movie Title (Year).ext` format
- Renames TV episodes to `Series Name S##E## - Episode Title.ext` format
- Renames music to `## - Track Title.ext` format
- Uses local LLM inference via LLamaSharp (no Ollama dependency)
- Supports Gemma, Phi, Llama, Qwen, and other GGUF models
- Preview mode to see suggestions before renaming
- Auto-rename on library scan (optional)
- Test rename feature in the admin UI
- API endpoints for programmatic access

## Requirements

- Jellyfin 10.11.0 or later
- Sufficient RAM for model loading (1B model needs ~2GB)

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
dotnet build -c Release
```

The plugin files will be in `bin/Release/net9.0/`. Copy all DLLs and `plugin.json` to your Jellyfin plugins directory, then restart Jellyfin.

### After Installation

1. Go to **Dashboard > Plugins > LLM File Renamer**
2. Download a model from the admin UI (see below)
3. Configure your preferences

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

| Model | Size | Memory | Description |
|-------|------|--------|-------------|
| **Gemma 3 1B** (Recommended) | ~700MB | ~2GB | Small, fast, good for basic renaming |
| Gemma 3 4B | ~2.5GB | ~4GB | Better quality, requires more RAM |
| Phi-3 Mini 4K | ~2.3GB | ~3GB | Good balance of speed and quality |
| Qwen 2.5 1.5B | ~1GB | ~2GB | Compact with good instruction following |
| Llama 3.2 1B | ~800MB | ~2GB | Fast and efficient |

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

All endpoints require admin authentication.

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
| `/LLMRenamer/Models/SetActive/{filename}` | POST | Set a model as active |
| `/LLMRenamer/Models/{filename}` | DELETE | Delete a local model |

### Rename Operations

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/LLMRenamer/Preview/{itemId}` | GET | Preview rename for an item |
| `/LLMRenamer/Rename/{itemId}` | POST | Execute rename for an item |
| `/LLMRenamer/Test` | POST | Test LLM with a sample filename |

### API Examples

**Download a model:**
```bash
curl -X POST "http://localhost:8096/LLMRenamer/Models/Download/gemma-3-1b-it-q4_0" \
  -H "X-Emby-Authorization: MediaBrowser Token=YOUR_TOKEN"
```

**Test rename:**
```bash
curl -X POST "http://localhost:8096/LLMRenamer/Test" \
  -H "X-Emby-Authorization: MediaBrowser Token=YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"Filename": "The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv"}'
```

**Download custom model:**
```bash
curl -X POST "http://localhost:8096/LLMRenamer/Models/DownloadCustom" \
  -H "X-Emby-Authorization: MediaBrowser Token=YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"Url": "https://example.com/model.gguf", "Filename": "my-model.gguf"}'
```

## Scheduled Task

The plugin adds a scheduled task "LLM File Renamer" under Library tasks. You can:
- Run it manually from Dashboard > Scheduled Tasks
- Configure triggers for automatic execution

## Troubleshooting

### Model fails to load
- Verify the model file is not corrupted (re-download if needed)
- Ensure you have enough RAM available
- Check Jellyfin logs for detailed error messages

### Download fails
- Check your internet connection
- Verify Jellyfin has write access to the plugin data directory
- Try downloading a smaller model first

### Poor rename suggestions
- Try a larger model (4B instead of 1B)
- Add custom prompt instructions in configuration
- Ensure metadata is properly scraped for your media

### High memory usage
- Use a smaller quantized model (Q4_0 or Q4_K_M)
- Reduce context size to 1024
- Unload model when not in use via API or restart Jellyfin

## Creating a Release

For maintainers, to create a new release:

1. Update the version in `plugin.json`
2. Build the release:
   ```bash
   cd Jellyfin.Plugin.LLMRenamer
   dotnet build -c Release
   ```
3. Create a zip file containing all files from `bin/Release/net9.0/`:
   ```bash
   cd bin/Release/net9.0
   zip -r jellyfin-llm-renamer-v1.0.0.zip .
   ```
4. Create a GitHub release with the zip file
5. Update `manifest.json` with:
   - New version entry
   - Updated `sourceUrl` pointing to the release zip
   - MD5 checksum of the zip file (`md5sum jellyfin-llm-renamer-v1.0.0.zip`)
   - Current timestamp

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

MIT License
