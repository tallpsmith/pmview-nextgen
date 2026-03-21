# pmview-nextgen Development Guidelines

Last updated: 2026-03-19

## Active Technologies
- C# (.NET 8.0 LTS) for all projects (Godot.NET.Sdk 4.6.1 pins to net8.0)
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

- **dotnet**: SDK at `/opt/homebrew/bin/dotnet`. If not on PATH: `export PATH="/opt/homebrew/bin:$PATH"`
- **Target framework**: All projects target `net8.0` (Godot.NET.Sdk 4.6.1 pins to net8.0). `Directory.Build.props` sets `RollForward=LatestMajor` so net8.0 binaries execute on any installed runtime.
- **Godot**: NOT installed in Claude Code VM (no UI). Write `.tscn`, `.gd`, and `.cs` files directly. User tests scenes in their host Godot editor.
- Always include `/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin` in PATH when running shell commands (VM quirk).

## GDScript Gotchas

- **Type inference on `find_child()` results**: Variables from `find_child()` are typed as `Node`. Using `:=` with methods like `.global_position` or `.project_ray_origin()` causes "Cannot infer type" parser errors. Always use explicit types: `var pos: Vector3 = (node as Node3D).global_position` or cast to the concrete type first: `var cam: Camera3D = _camera as Camera3D`.

## Addon Development Rules

- **Explicit using directives**: All C# files under `addons/pmview-bridge/` MUST use explicit `using` directives (`System`, `System.Collections.Generic`, `System.Threading.Tasks`, `System.Linq`, etc.). Never rely on `ImplicitUsings` — the addon gets installed into external Godot projects that may not have it enabled.

## Commands

```bash
# Build and test everything (CI filter excludes Godot app — no Godot SDK needed)
dotnet build pmview-nextgen.ci.slnf
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"

# Full solution (includes pmview-app — needs Godot SDK, use in Rider)
dotnet build pmview-nextgen.sln

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

C# (.NET 8.0) for all C# projects; GDScript for scene logic. Follow standard conventions.

## Documentation Scope

All specifications, brainstorming, and feature development **must** include documentation as part of the scope of work. Consider updates to:

- **README.md** — user-facing overview, quick start, feature descriptions
- **Architecture docs** — design decisions, layer diagrams, data flow
- **Installation & usage** — setup steps, CLI usage, configuration
- **Packaging / deployment / releasing** — build artefacts, distribution, versioning

Documentation is not an afterthought — it ships with the code.

## Third-Party Asset Attribution

When adding any external asset to this project (fonts, images, shaders, scripts, libraries bundled as files rather than NuGet packages):

1. **Include the asset's license file** alongside the asset (e.g. `OFL.txt` next to font files)
2. **Add an entry to the Acknowledgements table** in `README.md` with: asset name (linked to source), author, license (linked to local license file), and usage description
3. **Verify license compatibility** — only use assets with permissive licenses (MIT, Apache 2.0, SIL OFL, CC-BY, etc.)

Current bundled assets:
- `src/pmview-app/assets/fonts/PressStart2P-Regular.ttf` — SIL OFL 1.1 (license at `OFL.txt` in same directory)

## Pre-Push Checklist

**MANDATORY before every `git push`:**

1. **Run the local build and tests first.** Never push without a green local build:
   ```bash
   dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
   ```
   If the build or any test fails, fix it before pushing.

2. **Monitor GitHub CI after pushing.** Immediately after a successful push, launch a background agent to watch the GitHub Actions run for failures. Use the `gh` CLI to poll the workflow run status and report back if any jobs fail:
   ```
   Agent(subagent_type="general-purpose", run_in_background=true, prompt="Monitor the GitHub Actions CI run triggered by the push to branch '<branch-name>' in repo tallpsmith/pmview-nextgen. Use 'gh run list --branch <branch-name> --limit 1' to find the run, then poll with 'gh run watch <run-id>' or periodic 'gh run view <run-id>' until it completes. If any job fails, report the failing job name and the key error lines from the logs using 'gh run view <run-id> --log-failed'.")
   ```
   This ensures CI failures are caught and surfaced immediately rather than discovered later.

3. **Investigate ALL CI failures.** Even if a build failure appears unrelated to your changes, always investigate it and aim to fix. Flaky infrastructure, stale dependencies, or pre-existing breakage — none of these get a free pass. A red CI is everyone's problem.

