# Quickstart: PCP-to-Godot 3D Metrics Bridge

**Date:** 2026-03-05

## Dependencies

### macOS (Homebrew)

All dependencies installable via Homebrew. Run these before anything else:

```bash
# .NET 8 SDK (LTS) — required by PcpClient library and Godot C# build
brew install dotnet@8

# Godot 4.4+ with .NET/Mono support — the game engine
brew install --cask godot-mono

# Podman — container runtime for dev environment
brew install podman podman-compose

# Verify installations
dotnet --version          # should be 8.0.x
godot --version           # should be 4.4.x or later
podman --version
podman compose version
```

**Note:** Homebrew's `dotnet` formula (unversioned) tracks .NET 10. We pin to `dotnet@8` because Godot 4.4 requires .NET 8 as its minimum. When Godot 4.6 bumps to .NET 10, switch to `brew install dotnet`.

**Podman machine setup** (macOS requires a Linux VM for containers):

```bash
podman machine init
podman machine start
```

#### Summary of Homebrew packages

| Package | Type | Purpose |
|---------|------|---------|
| `dotnet@8` | Formula | .NET 8 SDK for building PcpClient and Godot C# |
| `godot-mono` | Cask | Godot 4.4+ with .NET support |
| `podman` | Formula | Container runtime (replaces Docker) |
| `podman-compose` | Formula | Compose file support for podman |
| `git` | Formula | Already installed on most macs; included for completeness |

### Linux Hints

Linux is the primary audience platform but a separate concern for full packaging. For development on Linux, the equivalents are:

| macOS (Homebrew) | Linux (apt/dnf) | Notes |
|------------------|-----------------|-------|
| `dotnet@8` | `dotnet-sdk-8.0` (Microsoft repo) | Add Microsoft's package feed first |
| `godot-mono` (cask) | Download from godotengine.org or Flatpak `org.godotengine.GodotSharp` | No standard distro package for .NET variant |
| `podman` | `podman` (native on Fedora/RHEL; `apt install podman` on Debian/Ubuntu) | No VM needed — runs natively |
| `podman-compose` | `podman-compose` (pip) or `podman compose` built-in (Podman 5+) | Check if `podman compose` works natively first |

Linux packaging (distributable builds, .desktop files, dependencies for end users) is a separate concern tracked outside this quickstart.

## 1. Clone and Branch

```bash
git clone <repo-url> pmview-nextgen
cd pmview-nextgen
git checkout 001-pcp-godot-bridge
```

## 2. Start the Dev Environment

The podman compose stack provides PCP, pmproxy, and Valkey with synthetic test data.

```bash
cd dev-environment
podman compose up -d
```

Verify pmproxy is responding:

```bash
curl -s http://localhost:44322/pmapi/metric?prefix=kernel.all.load | python3 -m json.tool
```

You should see metric metadata JSON including `kernel.all.load` with type `FLOAT` and semantics `instant`.

## 3. Build the PcpClient Library

```bash
cd src/pcp-client-dotnet
dotnet build
```

## 4. Run Tests

```bash
cd src/pcp-client-dotnet
dotnet test
```

Unit tests run without pmproxy (mocked HTTP). Integration tests require the dev environment stack.

## 5. Open the Godot Project

```bash
# From repo root
cd godot-project
godot --editor
```

On first open, Godot will generate the `.godot/` cache and build the C# solution.

## 6. First Run — Verify Metrics Flow

1. Ensure the dev environment is running (`podman compose up -d`)
2. Open the Godot editor
3. Open the scene referenced in your binding config (e.g., `scenes/pmview_classic.tscn`)
4. Press F5 (Play) — the scene should connect to pmproxy and begin displaying metric-driven visuals

## Project Layout Cheat Sheet

```
prototypes/          → Spike code (throwaway, not production)
src/pcp-client-dotnet/ → Standalone .NET library (xUnit tested)
godot-project/       → Godot 4.4+ project with C# bridge
  scripts/bridge/    → C# bridge layer (MetricPoller, BindingConfigLoader, SceneBinder)
  scenes/            → .tscn scene files
  bindings/          → .toml binding configuration files
dev-environment/     → Podman compose stack (PCP + pmproxy + Valkey)
```

## Common Tasks

### Fetch a metric manually (curl)

```bash
# Create a context
curl -s "http://localhost:44322/pmapi/context?polltimeout=60"
# → {"context": 12345, ...}

# Fetch a value using the context
curl -s "http://localhost:44322/pmapi/fetch?names=kernel.all.load&context=12345"

# Discover metrics under a namespace
curl -s "http://localhost:44322/pmapi/children?prefix=disk"

# Describe a metric
curl -s "http://localhost:44322/pmapi/metric?names=disk.dev.read"

# List instances
curl -s "http://localhost:44322/pmapi/indom?name=disk.dev.read"
```

### Rebuild everything

```bash
cd src/pcp-client-dotnet && dotnet build
cd godot-project && dotnet build
```

### Reset the dev environment

```bash
cd dev-environment
podman compose down -v
podman compose up -d
```

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `curl: (7) Failed to connect` to port 44322 | Dev environment not running. `podman compose up -d` |
| pmproxy returns empty metrics | Synthetic data not ingested yet. Check pmlogsynth container logs. |
| Godot C# build fails | Ensure .NET 8 SDK is installed (`dotnet --version`). Run `dotnet restore` in the Godot project dir. |
| `godot` command not found after cask install | Add to PATH: `export PATH="/Applications/Godot_mono.app/Contents/MacOS:$PATH"` or use the app directly. |
| `podman compose` not found | Ensure `podman-compose` is installed (`brew install podman-compose`). |
| Podman containers won't start on macOS | Run `podman machine init && podman machine start` — macOS needs a Linux VM. |
| Context expired errors | Increase `polltimeout` or decrease poll interval. Default 5s timeout is too short. |
| Scene nodes not updating | Check binding config: node paths must match scene tree exactly (case-sensitive). |
