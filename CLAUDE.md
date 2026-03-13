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
# Build and test everything (root solution includes all .NET projects)
dotnet build pmview-nextgen.sln
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"

# Run ALL tests including integration (requires dev-environment stack)
dotnet test pmview-nextgen.sln

# Run integration tests ONLY
dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "Category=Integration"

# Build Godot project C# (when godot-project/ exists)
dotnet build godot-project/pmview-nextgen.sln

# Generate a host-view scene from a running pmproxy
# Requires dev-environment stack running (podman compose up)
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  -o godot-project/scenes/host_view.tscn

# Generate into an external Godot project (copies addon automatically)
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  --install-addon \
  -o /path/to/my-godot-project/scenes/host_view.tscn
```

**Claude Code VM note:** The VM cannot reach the dev-environment Podman stack on the
host. Always use `--filter "FullyQualifiedName!~Integration"` when running tests from the VM.
This is the same filter used in GitHub Actions CI (no pmproxy in CI runners either).

## Code Style

C# (.NET 8.0 LTS) for PcpClient library and Godot bridge; GDScript for scene logic: Follow standard conventions

## Recent Changes
- 003-editor-pcp-bindings: Added C# (.NET 8.0 LTS) for bridge nodes + resources; GDScript for scene controller + Godot 4.4+ (Godot.NET.Sdk), existing PcpClient + PcpGodotBridge libraries
- 002-editor-launch-config: Added C# (.NET 8.0 LTS) for EditorPlugin + bridge nodes; GDScript for scene controller integration + Godot 4.4+ (Godot.NET.Sdk), existing PcpClient + PcpGodotBridge libraries

- 001-pcp-godot-bridge: Added C# (.NET 8.0 LTS) for PcpClient library and Godot bridge; GDScript for scene logic + Godot 4.4+ (Godot.NET.Sdk), System.Net.Http.HttpClient, System.Text.Json, Tomlyn (TOML parser)

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
