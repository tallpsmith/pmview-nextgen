# pmview-nextgen Development Guidelines

Last updated: 2026-03-16

## Active Technologies
- C# (.NET 8.0 LTS) for Godot bridge nodes/resources + PcpClient/PcpGodotBridge libraries
- C# (.NET 10.0) for Host Projector CLI + all test projects
- GDScript for scene controllers, camera, building blocks
- Godot 4.6+ (Godot.NET.Sdk 4.6.1), System.Net.Http.HttpClient, System.Text.Json, Tomlyn

## Project Structure

```text
src/
  pcp-client-dotnet/          # PcpClient: pmproxy HTTP/JSON client
  pcp-godot-bridge/           # PcpGodotBridge: binding model + validation
  pmview-host-projector/      # Host Projector CLI: topology → .tscn + project scaffolding
  pmview-bridge-addon/        # Godot addon: bridge plugin + building blocks + gdUnit4 tests
```

## Environment

- **dotnet**: .NET 10 SDK at `/opt/homebrew/bin/dotnet`. If not on PATH: `export PATH="/opt/homebrew/bin:$PATH"`
- **Target frameworks**: Libraries consumed by Godot (PcpClient, PcpGodotBridge) target `net8.0` (Godot.NET.Sdk/4.6.1 is pinned to net8.0). Standalone executables and test projects target `net10.0`.
- **Godot**: NOT installed in Claude Code VM (no UI). Write `.tscn`, `.gd`, and `.cs` files directly. User tests scenes in their host Godot editor.
- Always include `/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin` in PATH when running shell commands (VM quirk).

## Addon Development Rules

- **Explicit using directives**: All C# files under `addons/pmview-bridge/` MUST use explicit `using` directives (`System`, `System.Collections.Generic`, `System.Threading.Tasks`, `System.Linq`, etc.). Never rely on `ImplicitUsings` — the addon gets installed into external Godot projects that may not have it enabled.

## Commands

```bash
# Build and test everything (root solution includes all .NET projects)
dotnet build pmview-nextgen.sln
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"

# Run ALL tests including integration (requires dev-environment stack)
dotnet test pmview-nextgen.sln

# Run integration tests ONLY
dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "Category=Integration"

# Build Godot addon C# (from the addon dev workspace)
dotnet build src/pmview-bridge-addon/pmview-nextgen.sln

# Scaffold a new Godot project from scratch (project.godot, .csproj, .sln, main.tscn, addon)
# Requires dev-environment stack running (podman compose up)
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  init /path/to/my-new-project

# Generate a host-view scene into an existing Godot project
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  -o /path/to/my-godot-project/scenes/host_view.tscn

# Generate + auto-scaffold if no project exists yet
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --init --pmproxy http://localhost:44322 \
  -o /path/to/my-new-project/scenes/host_view.tscn
```

**Claude Code VM note:** The VM cannot reach the dev-environment Podman stack on the
host. Always use `--filter "FullyQualifiedName!~Integration"` when running tests from the VM.
This is the same filter used in GitHub Actions CI (no pmproxy in CI runners either).

## Code Style

C# (.NET 8.0) for Godot libraries, C# (.NET 10.0) for CLI/tests; GDScript for scene logic. Follow standard conventions.

<!-- MANUAL ADDITIONS START -->

## Pre-Push Checklist

**MANDATORY before every `git push`:**

1. **Run the local build and tests first.** Never push without a green local build:
   ```bash
   dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"
   ```
   If the build or any test fails, fix it before pushing.

2. **Monitor GitHub CI after pushing.** Immediately after a successful push, launch a background agent to watch the GitHub Actions run for failures. Use the `gh` CLI to poll the workflow run status and report back if any jobs fail:
   ```
   Agent(subagent_type="general-purpose", run_in_background=true, prompt="Monitor the GitHub Actions CI run triggered by the push to branch '<branch-name>' in repo tallpsmith/pmview-nextgen. Use 'gh run list --branch <branch-name> --limit 1' to find the run, then poll with 'gh run watch <run-id>' or periodic 'gh run view <run-id>' until it completes. If any job fails, report the failing job name and the key error lines from the logs using 'gh run view <run-id> --log-failed'.")
   ```
   This ensures CI failures are caught and surfaced immediately rather than discovered later.

<!-- MANUAL ADDITIONS END -->

