include .env

PROJECT := Jellyfin.Plugin.LLMRenamer/Jellyfin.Plugin.LLMRenamer.csproj
BUILD_DIR := Jellyfin.Plugin.LLMRenamer/bin/Release/net9.0
AUTH_HEADER := Authorization: MediaBrowser Token="$(API_TOKEN)"

# Find the latest plugin directory
PLUGIN_DIR = $(shell ls -d "$(PLUGIN_BASE)/LLM File Renamer"_* 2>/dev/null | sort -V | tail -1)

.PHONY: build deploy restart shutdown status logs jf-logs clean-old-plugins release download-native download-model setup

## Build the plugin
build:
	$(DOTNET) build -c Release $(PROJECT)

## Deploy: build, stop Jellyfin, copy DLL. Start Jellyfin manually after.
deploy: build
	@echo "==> Shutting down Jellyfin..."
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/System/Shutdown" > /dev/null 2>&1 || true
	@echo "==> Waiting for Jellyfin to stop..."
	@sleep 5
	@echo "==> Copying DLL to $(PLUGIN_DIR)..."
	@cp "$(BUILD_DIR)/Jellyfin.Plugin.LLMRenamer.dll" "$(PLUGIN_DIR)/"
	@echo "==> Deploy complete. Start Jellyfin manually."

## Restart Jellyfin
restart:
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/System/Restart" -w "\nHTTP: %{http_code}\n"

## Shutdown Jellyfin
shutdown:
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/System/Shutdown" -w "\nHTTP: %{http_code}\n"

## Show plugin status
status:
	@curl -s -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Status" 2>/dev/null | python3 -m json.tool || echo "Jellyfin not reachable"

## Show plugin logs (today)
logs:
	@cat /mnt/c/ProgramData/Jellyfin/Server/log/LLMRenamer_$$(date +%Y-%m-%d).log 2>/dev/null || echo "No plugin log for today"

## Show latest Jellyfin log entries for the plugin
jf-logs:
	@grep -i "LLMRenamer\|LLM File\|FileRenamer" /mnt/c/ProgramData/Jellyfin/Server/log/log_$$(date +%Y%m%d).log 2>/dev/null | tail -30

## Remove old plugin directories (keeps only the latest)
clean-old-plugins:
	@echo "Plugin directories:"
	@ls -d "$(PLUGIN_BASE)/LLM File Renamer"_* 2>/dev/null
	@echo ""
	@echo "Keeping: $(PLUGIN_DIR)"
	@for dir in $$(ls -d "$(PLUGIN_BASE)/LLM File Renamer"_* 2>/dev/null | sort -V | head -n -1); do \
		echo "Removing: $$dir"; \
		rm -rf "$$dir"; \
	done

## Download CPU native libraries via Jellyfin API
download-native:
	@echo "==> Starting native library download (CPU)..."
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Native/Download" | python3 -m json.tool
	@echo "==> Polling progress..."
	@while true; do \
		RESP=$$(curl -s -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Models/DownloadProgress"); \
		STATE=$$(echo "$$RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('State',''))" 2>/dev/null); \
		STATUS=$$(echo "$$RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('Status',''))" 2>/dev/null); \
		echo "    $$STATUS"; \
		if [ "$$STATE" = "Completed" ] || [ "$$STATE" = "Failed" ] || [ "$$STATE" = "Cancelled" ] || [ -z "$$STATE" ]; then \
			break; \
		fi; \
		sleep 1; \
	done
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Models/ClearDownloadStatus" > /dev/null 2>&1

## Download CUDA (GPU) native libraries via Jellyfin API
download-native-cuda:
	@echo "==> Starting native library download (CUDA/GPU)..."
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Native/Download?cuda=true" | python3 -m json.tool
	@echo "==> Polling progress..."
	@while true; do \
		RESP=$$(curl -s -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Models/DownloadProgress"); \
		STATE=$$(echo "$$RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('State',''))" 2>/dev/null); \
		STATUS=$$(echo "$$RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('Status',''))" 2>/dev/null); \
		echo "    $$STATUS"; \
		if [ "$$STATE" = "Completed" ] || [ "$$STATE" = "Failed" ] || [ "$$STATE" = "Cancelled" ] || [ -z "$$STATE" ]; then \
			break; \
		fi; \
		sleep 1; \
	done
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Models/ClearDownloadStatus" > /dev/null 2>&1

## Download default model via Jellyfin API
download-model:
	@echo "==> Starting model download (qwen2.5-3b-instruct-q4_k_m)..."
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Models/Download/qwen2.5-3b-instruct-q4_k_m" | python3 -m json.tool
	@echo "==> Polling progress..."
	@while true; do \
		RESP=$$(curl -s -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Models/DownloadProgress"); \
		STATE=$$(echo "$$RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('State',''))" 2>/dev/null); \
		PCT=$$(echo "$$RESP" | python3 -c "import sys,json; print('{:.1f}'.format(json.load(sys.stdin).get('Percentage',0)))" 2>/dev/null); \
		STATUS=$$(echo "$$RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('Status',''))" 2>/dev/null); \
		printf "\r    %s%% - %s" "$$PCT" "$$STATUS"; \
		if [ "$$STATE" = "Completed" ] || [ "$$STATE" = "Failed" ] || [ "$$STATE" = "Cancelled" ] || [ -z "$$STATE" ]; then \
			echo ""; \
			break; \
		fi; \
		sleep 2; \
	done
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Models/ClearDownloadStatus" > /dev/null 2>&1
	@echo "==> Setting model as active..."
	@curl -s -X POST -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/LLMRenamer/Models/SetActive/qwen2.5-3b-instruct-q4_k_m.gguf" | python3 -m json.tool

## Full setup (CPU): download native libs + model
setup: download-native download-model

## Full setup (GPU): download CUDA native libs + model, set GPU layers to 99
setup-gpu: download-native-cuda download-model
	@echo "==> Setting GPU layers to 99..."
	@curl -s -H '$(AUTH_HEADER)' "$(JELLYFIN_URL)/Plugins/a1b2c3d4-e5f6-7890-abcd-ef1234567890/Configuration" | \
		python3 -c "import sys,json; c=json.load(sys.stdin); c['GpuLayerCount']=99; print(json.dumps(c))" | \
		curl -s -X POST -H '$(AUTH_HEADER)' -H 'Content-Type: application/json' \
			"$(JELLYFIN_URL)/Plugins/a1b2c3d4-e5f6-7890-abcd-ef1234567890/Configuration" -d @- > /dev/null
	@echo "==> GPU setup complete. Restart Jellyfin to apply."

## Create a release tag and push
release:
	@if [ -z "$(VERSION)" ]; then \
		echo "Usage: make release VERSION=0.0.22"; \
		exit 1; \
	fi
	git tag v$(VERSION)
	git push origin v$(VERSION)
	@echo "Release v$(VERSION) tagged and pushed. GitHub Actions will handle the rest."
