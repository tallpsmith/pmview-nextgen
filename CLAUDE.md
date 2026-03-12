# pmview-nextgen Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-05

## Active Technologies
- C# (.NET 8.0 LTS) for EditorPlugin + bridge nodes; GDScript for scene controller integration + Godot 4.4+ (Godot.NET.Sdk), existing PcpClient + PcpGodotBridge libraries (002-editor-launch-config)
- Godot ProjectSettings (`project.godot` file — automatic persistence) (002-editor-launch-config)
- C# (.NET 8.0 LTS) for bridge nodes + resources; GDScript for scene controller + Godot 4.4+ (Godot.NET.Sdk), existing PcpClient + PcpGodotBridge libraries (003-editor-pcp-bindings)
- Godot Custom Resources serialized inline in `.tscn` scene files (003-editor-pcp-bindings)

- C# (.NET 8.0 LTS) for PcpClient library and Godot bridge; GDScript for scene logic + Godot 4.4+ (Godot.NET.Sdk), System.Net.Http.HttpClient, System.Text.Json, Tomlyn (TOML parser) (001-pcp-godot-bridge)

## Project Structure

```text
src/
tests/
```

## Environment

- **dotnet**: if `dotnet` is NOT on the path, you need to `export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"` and `export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"`
- **Godot**: NOT installed in Claude Code VM (no UI). Write `.tscn`, `.gd`, and `.cs` files directly. User tests scenes in their host Godot editor.
- Always include `/usr/bin:/bin:/usr/sbin:/sbin` in PATH when running shell commands (VM quirk).

## Commands

```bash
# Build PcpClient library
dotnet build src/pcp-client-dotnet/PcpClient.sln

# Run PcpClient unit tests
dotnet test src/pcp-client-dotnet/PcpClient.sln

# Build Godot project C# (when godot-project/ exists)
dotnet build godot-project/pmview-nextgen.sln
```

## Code Style

C# (.NET 8.0 LTS) for PcpClient library and Godot bridge; GDScript for scene logic: Follow standard conventions

## Recent Changes
- 003-editor-pcp-bindings: Added C# (.NET 8.0 LTS) for bridge nodes + resources; GDScript for scene controller + Godot 4.4+ (Godot.NET.Sdk), existing PcpClient + PcpGodotBridge libraries
- 002-editor-launch-config: Added C# (.NET 8.0 LTS) for EditorPlugin + bridge nodes; GDScript for scene controller integration + Godot 4.4+ (Godot.NET.Sdk), existing PcpClient + PcpGodotBridge libraries

- 001-pcp-godot-bridge: Added C# (.NET 8.0 LTS) for PcpClient library and Godot bridge; GDScript for scene logic + Godot 4.4+ (Godot.NET.Sdk), System.Net.Http.HttpClient, System.Text.Json, Tomlyn (TOML parser)

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
