# Contributing to pmview-nextgen

## Prerequisites

- [.NET 9.0+ SDK](https://dotnet.microsoft.com/download/dotnet/9.0) — the .NET 9 SDK builds our net8.0-targeted libraries; Godot 4.6 requires it
- [Godot 4.6+](https://godotengine.org/download) with .NET support (the Mono/C# flavour)
- [Performance Co-Pilot (PCP)](https://pcp.io/) with pmproxy running (for live data)
- Docker or Podman (for the dev-environment stack)

## Build & Test

```bash
# Build everything
dotnet build pmview-nextgen.sln

# Unit tests only (no dev stack needed — use this in CI or locally by default)
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"

# All tests including integration (requires dev stack running)
dotnet test pmview-nextgen.sln

# Integration tests only
dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "Category=Integration"

# Build the addon C# assemblies (addon development workspace)
dotnet build src/pmview-bridge-addon/pmview-nextgen.sln
```

## Dev Environment Stack

The dev stack provides a pmproxy endpoint at `http://localhost:44322` with synthetic metric data — no real PCP installation needed for development.

```bash
cd dev-environment
docker compose up -d
```

This runs:
- **pmcd + pmlogger** — PCP collection daemon with synthetic data via pmlogsynth
- **pmproxy** — REST API gateway (port 44322)
- **valkey** — Redis-compatible cache used by pmproxy

## Project Structure

```
pmview-nextgen/
├── pmview-nextgen.sln                  # Root solution (all .NET projects)
├── src/
│   ├── pcp-client-dotnet/              # PcpClient: pmproxy HTTP/JSON client
│   │   ├── src/PcpClient/
│   │   └── tests/PcpClient.Tests/
│   ├── pcp-godot-bridge/               # PcpGodotBridge: binding model + validation
│   │   ├── src/PcpGodotBridge/
│   │   └── tests/PcpGodotBridge.Tests/
│   └── pmview-host-projector/          # Host Projector: topology → .tscn generator
│       ├── src/PmviewHostProjector/
│       └── tests/PmviewHostProjector.Tests/
│   └── pmview-bridge-addon/            # Addon development workspace (Godot project)
│       ├── addons/pmview-bridge/       # Self-contained addon (copied to target projects)
│       │   ├── *.cs                    # Bridge plugin (Poller, Binder, Bindable, Inspector)
│       │   └── building_blocks/        # GroundedBar/Cylinder, GridLayout3D, ZoneLabel
│       ├── test/                       # gdUnit4 tests
│       ├── pmview-nextgen.csproj
│       └── pmview-nextgen.sln
├── dev-environment/                    # Docker compose stack
├── specs/                              # Feature specifications
└── docs/                              # Design documents and plans
```

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full layer breakdown.

## .NET Framework Targets

| Project type | Target |
|---|---|
| Libraries consumed by Godot (PcpClient, PcpGodotBridge) | `net8.0` (pinned by Godot.NET.Sdk 4.6.1) |
| Standalone executables and test projects | `net10.0` |

## Addon C# Rules

All C# files under `addons/pmview-bridge/` **must** use explicit `using` directives (`System`, `System.Collections.Generic`, `System.Threading.Tasks`, `System.Linq`, etc.). Never rely on `ImplicitUsings` — the addon gets installed into external Godot projects that may not have it enabled.
