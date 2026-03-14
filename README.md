# pmview-nextgen

**Bringing Performance Monitoring to Life**

`pmview-nextgen` transforms system performance metrics into living, breathing 3D environments. Inspired by the original PCP pmview, it restores humanity to system monitoring — bridging how systems naturally behave (alive, flowing, rhythmic) with how human brains naturally comprehend (spatial, environmental, emotional).

> **"Make people see their system alive and say 'oh wow'"**

## Core Philosophy

- **Systems are already alive** — they react, flow, have rhythms, respond to stimulus
- **Humans are optimized for spatial/environmental pattern recognition** — not data grids
- **Transform passive monitoring into active curiosity** — make people curious about their data
- **Bring people together** — technical and non-technical united through shared wonder
- **Augment, don't replace** — complement existing monitoring with team culture fun

## Quick Start

**Prerequisites:** [.NET 9.0+ SDK](https://dotnet.microsoft.com/download/dotnet/9.0), [Godot 4.6+](https://godotengine.org/download) with .NET/Mono support

The quick start below uses the bundled dev stack for synthetic metric data (requires Docker or Podman). If you have a real PCP/pmproxy endpoint, skip the `docker compose` step and point `--pmproxy` at it directly.

```bash
git clone https://github.com/tallpsmith/pmview-nextgen.git
cd pmview-nextgen

# Build and test
dotnet build pmview-nextgen.sln
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"

# Start the dev stack (PCP + pmproxy + synthetic data)
cd dev-environment && docker compose up -d && cd ..

# Generate a host-view scene into the included Godot project
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  -o godot-project/scenes/host_view.tscn
```

Open `godot-project/` in Godot, build (Ctrl+B), and run the scene.

To generate into **your own Godot project**, see [docs/HOST-PROJECTOR.md](docs/HOST-PROJECTOR.md).

## What You'll See

The generated host view covers 8 metric zones across foreground and background rows:

- **Load** — 1/5/15 minute load averages as vertical bars
- **CPU** — User/Sys/Nice as bars; per-core grid in the background
- **Memory** — Used/Cached/Buffers (auto-ranged to physical RAM)
- **Disk** — Read/Write bytes as cylinders; per-device grid in the background
- **Network In/Out** — Bytes/Packets/Errors per interface

## Heritage

This project modernizes the original [PCP pmview](https://pcp.io/), which visualized system metrics as 3D shapes — colored cylinders for disks, cubes for CPU/Memory/Load. `pmview-nextgen` takes this concept to the next level with game-like, living, breathing worlds.

## Documentation

| Doc | Contents |
|-----|----------|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | System architecture, layers, project structure |
| [docs/BINDINGS.md](docs/BINDINGS.md) | How to wire PCP metrics to scene properties |
| [docs/HOST-PROJECTOR.md](docs/HOST-PROJECTOR.md) | CLI scene generator reference |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Dev environment setup, build & test workflow |

## License

TBD — Exploring open source and potential dual licensing options

## Contact

Paul Smith — Project Creator
