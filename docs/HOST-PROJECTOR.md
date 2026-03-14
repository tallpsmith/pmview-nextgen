# Host Projector

`pmview-host-projector` is a CLI tool that connects to a live pmproxy, discovers the host's metric topology (CPUs, disks, network interfaces, memory), and generates a complete Godot `.tscn` scene — complete with PcpBindable bindings, layout, camera, and lighting.

## Basic Usage

```bash
# Generate into the included godot-project/ (addon already installed)
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  -o godot-project/scenes/host_view.tscn

# Generate into your own Godot project
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://myserver:44322 \
  --install-addon \
  -o /path/to/my-godot-project/scenes/host_view.tscn
```

## Installing into Your Own Godot Project

The generated scene references resources from the `pmview-bridge` addon (`addons/pmview-bridge/`). The included `godot-project/` already has it. For external projects, use `--install-addon`.

**Before running:** Create the C# solution in Godot first:
`Project → Tools → C# → Create C# Solution`

This creates a `.csproj` for the projector to patch.

When `--install-addon` is used, the projector:
1. Builds `PcpClient.dll`, `PcpGodotBridge.dll`, and `Tomlyn.dll` from source
2. Copies them into `addons/pmview-bridge/lib/` in the target project
3. Patches the target `.csproj` with `<Reference>` entries pointing at the bundled DLLs

After generation:
1. Open the project in Godot
2. Build C# (Ctrl+B)
3. Enable the plugin: `Project Settings → Plugins → pmview-bridge → Enable`
4. Open and run the generated scene

## Generated Scene Layout

The host view arranges 8 metric zones across two rows:

| Zone | Row | Metrics |
|------|-----|---------|
| Load | Foreground | 1/5/15 minute load averages (bars) |
| CPU | Foreground | User/Sys/Nice (bars) |
| Memory | Foreground | Used/Cached/Buffers (bars, auto-ranged to physical RAM) |
| Disk | Foreground | Read/Write bytes (cylinders) |
| Per-CPU | Background | User/Sys/Nice per CPU core (grid) |
| Per-Disk | Background | Read/Write per device (grid) |
| Network In | Background | Bytes/Packets/Errors per interface (grid) |
| Network Out | Background | Bytes/Packets/Errors per interface (grid) |

Foreground zones use larger individual shapes for at-a-glance reading. Background zones use grids scaled to the actual number of cores/devices/interfaces discovered at generation time.

## CLI Reference

| Flag | Description |
|------|-------------|
| `--pmproxy <url>` | pmproxy base URL (required) |
| `-o <path>` | Path to the output `.tscn` file — must be inside an existing Godot project directory |
| `--install-addon` | Seed the target Godot project with the `pmview-bridge` addon (see below) |

The projector requires pmproxy to be reachable at generation time — it queries the REST API to discover actual topology (number of CPUs, disk names, network interfaces) so the scene is sized correctly.

### `--install-addon`

Use this flag the first time you target a new Godot project to install the `pmview-bridge` addon and its .NET dependencies. It is idempotent — safe to run on subsequent generations if you want to pick up addon updates, it will not corrupt an already-working project.
