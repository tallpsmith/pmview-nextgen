# Quickstart: Editor Launch Configuration

**Feature Branch**: `002-editor-launch-config` | **Date**: 2026-03-11

## Prerequisites

- Godot 4.4+ with .NET 8 support
- Existing pmview-nextgen project with working scenes
- Running pmproxy instance (or dev-environment podman stack)

## What This Feature Does

Adds a Godot editor plugin that lets you configure PCP world settings (endpoint, live/archive mode, timestamp, speed, loop) in the Godot Project Settings before pressing Play. No more fumbling with runtime overlays.

## After Implementation

### Enable the Plugin

1. Open Godot editor
2. Go to **Project > Project Settings > Plugins**
3. Enable **pmview-bridge**

### Configure World Settings

1. Go to **Project > Project Settings > General**
2. Scroll to (or search for) the **pmview/** category
3. Set your preferences:
   - **Endpoint**: pmproxy URL (default: `http://localhost:44322`)
   - **Mode**: Archive (default) or Live
   - **Archive Start Timestamp**: ISO 8601 string (empty = 24h ago)
   - **Archive Speed**: 0.1x–100x (default: 10x)
   - **Archive Loop**: On/Off (default: Off)

### Launch a Scene

Press Play. The scene immediately connects to the configured endpoint and starts in the configured mode. No runtime interaction needed.

### Runtime Overrides

The F3 playback overlay still works if you need to change settings mid-session (FR-010).

## Development Setup

```bash
# Build everything
dotnet build src/pcp-client-dotnet/PcpClient.sln
dotnet test src/pcp-client-dotnet/PcpClient.sln

# Start dev environment
cd dev-environment && podman compose up -d

# Open Godot project
# (User opens godot-project/ in their Godot editor)
```

## Key Files

| File | Purpose |
|------|---------|
| `addons/pmview-bridge/plugin.cfg` | Plugin manifest |
| `addons/pmview-bridge/PmviewBridgePlugin.cs` | EditorPlugin (settings registration) |
| `addons/pmview-bridge/MetricPoller.cs` | Metric polling + playback |
| `addons/pmview-bridge/SceneBinder.cs` | Metric → scene node binding |
| `addons/pmview-bridge/MetricBrowser.cs` | Metric discovery |
| `scripts/scenes/metric_scene_controller.gd` | Reads settings, orchestrates launch |
