# Implementation Plan: PCP-to-Godot 3D Metrics Bridge

**Branch**: `001-pcp-godot-bridge` | **Date**: 2026-03-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-pcp-godot-bridge/spec.md`

## Summary

Bridge PCP performance metrics into live 3D Godot scenes for SRE monitoring. A standalone .NET library (`PcpClient`) communicates with pmproxy's REST API, a thin C# bridge layer inside Godot polls metrics and drives scene objects, and GDScript scenes provide the 3D visualisation. A TOML-based binding configuration maps scene nodes to metrics and visual properties, enabling data-driven scene composition without code changes.

The implementation follows the constitution's prototype-first mandate: three spikes validate feasibility (connectivity, scene binding, AI world crafting), then production code is TDD'd from scratch informed by spike learnings.

## Technical Context

**Language/Version**: C# (.NET 8.0 LTS) for PcpClient library and Godot bridge; GDScript for scene logic
**Primary Dependencies**: Godot 4.4+ (Godot.NET.Sdk), System.Net.Http.HttpClient, System.Text.Json, Tomlyn (TOML parser)
**Storage**: N/A — no persistent storage; all data is live from pmproxy or replayed from PCP archives via Valkey
**Testing**: xUnit for PcpClient library (`dotnet test`); GdUnit4Net for bridge layer if needed; manual + GdUnit4 for GDScript scenes
**Target Platform**: Linux desktop (primary audience), macOS desktop (development environment)
**Project Type**: Desktop application (Godot 4.4+) + standalone .NET class library
**Performance Goals**: 30+ FPS sustained (60 FPS target); <2s metric-to-visual latency; stable memory over 1-hour sessions
**Constraints**: Cross-platform Linux/macOS; no Godot dependency in PcpClient; pmproxy always network-accessed (no embedded mode)
**Scale/Scope**: 50+ concurrent metric instances without visual degradation; single endpoint at a time

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate (Phase 0 Entry)

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Prototype-First | PASS | Three spikes defined (connectivity, scene binding, AI world crafting). Prototype code isolated in `prototypes/` directory. Spike findings feed back into spec before production implementation. |
| II. TDD (Non-Negotiable) | PASS | PcpClient library will be TDD'd with xUnit after spikes. Prototypes exempt per constitution. No production code without failing tests first. |
| III. Code Quality | PASS | Three-layer separation (GDScript scenes / C# bridge / PcpClient library) enforces SRP. PcpClient knows PCP, not Godot. Bridge is thin wiring. |
| IV. UX Consistency | PASS | Binding configuration vocabulary will be documented. Normalisation ranges defined per binding. Deferred to post-spike design phase. |
| V. Performance Standards | PASS | 30+ FPS target, <2s latency target defined. Polling interval configurable. Performance benchmarks planned for CI. |

### Post-Design Gate (Phase 1 Exit) — Re-evaluated

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Prototype-First | PASS | Spike sequence unchanged. Each spike has a clear completion gate. |
| II. TDD | PASS | PcpClient test strategy defined: xUnit, no Godot runtime needed. Bridge kept thin to minimise untestable surface. |
| III. Code Quality | PASS | Data model entities map cleanly to PCP domain concepts. Binding config is declarative TOML — no code changes for new scenes. |
| IV. UX Consistency | PASS | Binding vocabulary (height, rotation_speed, color, scale) documented in data model. Normalisation is explicit per binding. |
| V. Performance Standards | PASS | Polling architecture avoids blocking the render loop. Context management prevents pmproxy session churn. |

## Project Structure

### Documentation (this feature)

```text
specs/001-pcp-godot-bridge/
├── plan.md              # This file
├── research.md          # Phase 0 output — pmproxy API, Godot C#, config format research
├── data-model.md        # Phase 1 output — entities, relationships, validation rules
├── quickstart.md        # Phase 1 output — dev environment setup and first run
├── contracts/           # Phase 1 output — PcpClient public API, binding config schema
│   ├── pcpclient-api.md
│   └── binding-config-schema.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
prototypes/
├── spike-01-connectivity/     # Minimal C# in Godot → pmproxy fetch
├── spike-02-scene-binding/    # Real metric drives 3D scene object
└── spike-03-world-crafting/   # godot-mcp evaluation

src/
└── pcp-client-dotnet/
    ├── PcpClient.sln
    ├── src/
    │   └── PcpClient/
    │       ├── PcpClient.csproj       # net8.0 classlib, NO Godot deps
    │       ├── PcpContext.cs           # Context lifecycle management
    │       ├── MetricDescriptor.cs     # Metric metadata model
    │       ├── MetricValue.cs          # Fetched value model
    │       ├── InstanceDomain.cs       # Instance domain model
    │       ├── PcpMetricFetcher.cs     # HTTP fetch operations
    │       ├── PcpMetricDiscovery.cs   # Namespace traversal and description
    │       └── IPcpClient.cs           # Public interface
    └── tests/
        └── PcpClient.Tests/
            ├── PcpClient.Tests.csproj  # xUnit, references PcpClient
            ├── PcpContextTests.cs
            ├── MetricFetcherTests.cs
            └── MetricDiscoveryTests.cs

godot-project/                          # Godot 4.4+ project (replaces godot-prototype/)
├── project.godot
├── pmview-nextgen.csproj               # Godot.NET.Sdk, references PcpClient
├── pmview-nextgen.sln                  # Includes Godot project + PcpClient + tests
├── scripts/
│   ├── bridge/
│   │   ├── MetricPoller.cs             # C# bridge: polls PcpClient, emits signals
│   │   ├── BindingConfigLoader.cs      # Parses TOML binding config
│   │   └── SceneBinder.cs             # Applies metric values to scene nodes
│   └── scenes/
│       └── *.gd                        # GDScript scene scripts
├── scenes/
│   └── *.tscn                          # Godot scene files
└── bindings/
    └── *.toml                          # Binding configuration files

dev-environment/
├── compose.yml                         # Podman compose: pcp + pmproxy + valkey
├── pmlogsynth/
│   └── configs/                        # Synthetic data generation configs
└── README.md
```

**Structure Decision**: Custom multi-project layout matching the approved architecture (architecture design doc, 2026-03-05). Three distinct layers: standalone .NET library, Godot C# bridge, GDScript scenes. The `godot-prototype/` directory will be superseded by `godot-project/` once spikes complete and production structure is established. Spike code lives in `prototypes/` and does not graduate to production per constitution.

## Complexity Tracking

> No constitution violations detected. All design decisions align with principles I–V.

| Decision | Justification | Simpler Alternative Considered |
|----------|---------------|-------------------------------|
| Separate PcpClient library | Enables xUnit testing without Godot runtime; future NuGet independence | Inline C# in Godot project — rejected because it couples PCP logic to Godot and prevents independent testing |
| TOML binding config | Human-editable, comment-supporting, unambiguous spec | JSON (no comments, painful to hand-edit), .tres (Godot-locked, no xUnit testing), YAML (ambiguity traps) |
| Explicit pmproxy context management | Predictable session lifecycle, avoids implicit context leaks | Implicit hostspec per request — rejected because it creates server-side context sprawl |
