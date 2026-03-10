# Research: PCP-to-Godot 3D Metrics Bridge

**Date:** 2026-03-05
**Status:** Complete
**Feature:** 001-pcp-godot-bridge

## R1: pmproxy REST API Surface

### Decision: Use `/pmapi/*` for live data, `/series/*` for historical playback

### Rationale
pmproxy exposes four endpoint groups. Two are relevant:

- **`/pmapi/*`** — Live metric access via server-side contexts. Connects to pmcd directly. No backend required. This covers FR-001 through FR-006 (live fetch, discovery, instance domains).
- **`/series/*`** — Time-series queries against a Valkey-backed archive. Required for FR-013/FR-014 (time cursor playback). Uses a pmseries query language with time range selectors.

The other two (`/search/*` for full-text search, `/metrics` for OpenMetrics export) are not needed initially.

### Key API Endpoints

| Endpoint | Purpose | Notes |
|----------|---------|-------|
| `GET /pmapi/context` | Create session | Default `polltimeout` is **5 seconds** — must set higher |
| `GET /pmapi/fetch?names=...&context=N` | Fetch current values | Returns JSON with instances array |
| `GET /pmapi/metric?names=...` | Metric metadata/description | Type, semantics, units, help text |
| `GET /pmapi/indom?name=...` | Instance domain enumeration | Per-CPU, per-disk, etc. |
| `GET /pmapi/children?prefix=...` | PMNS namespace traversal | Tree walk for discovery |
| `GET /series/query?expr=...` | Historical time-series query | Requires Valkey backend |
| `GET /series/values?series=...` | Fetch historical values | By SHA-1 series identifiers |

### Critical Quirks

1. **Context timeout default is 5 seconds.** Set `polltimeout` to 30-120s or context evaporates between polls.
2. **Two distinct data paths:** `/pmapi/*` hits pmcd for live; `/series/*` requires Valkey with ingested archives. Both needed.
3. **No authentication by default.** Wide open HTTP. SASL/TLS optional. Matches FR-015.
4. **No rate limiting.** Poll responsibly.
5. **Everything is GET** except `/pmapi/derive` (POST). Even store operations. Don't ask.
6. **Instance `null`** means a singular metric (no instance domain).
7. **Timestamps** are floating-point epoch seconds with microsecond precision.

### Alternatives Considered
- **Direct pmcd protocol:** Too complex, binary protocol, no benefit over REST for this use case.
- **OpenMetrics `/metrics` endpoint:** Prometheus-compatible text format. Loses PCP-native naming (dots become underscores) and instance domain semantics. Rejected.

---

## R2: Godot 4.x C# Integration

### Decision: .NET 8 standalone library + thin C# bridge in Godot

### Rationale
Godot 4.4+ requires .NET 8 minimum. The engine uses .NET Core hosting APIs (not old Mono). Standard `System.Net.Http.HttpClient` works on desktop (Linux/macOS) without issues.

### Key Findings

| Aspect | Detail |
|--------|--------|
| .NET version | .NET 8 (LTS) minimum as of Godot 4.4. .NET 10 LTS expected in Godot 4.6. |
| C# scripts | Must be `partial` classes extending Godot node types |
| GDScript↔C# interop | C# public methods callable from GDScript directly. C# calls GDScript via `Call()`/`Get()`. |
| async/await | Works in Godot C#. Signal handlers can be `async void`. `HttpClient` returns to main thread after `await`. |
| Cross-language async | **Does NOT work.** Use Godot Signals for async communication across language boundary. |
| External .NET library | Standard `<ProjectReference>` in `.csproj`. No Godot dependency needed in the library. |
| HttpClient | Works on desktop. Known Android SSL bug (irrelevant for us). |

### Testing Strategy

| Layer | Framework | Godot Runtime? |
|-------|-----------|---------------|
| PcpClient library | **xUnit** + `dotnet test` | No |
| C# Bridge layer | GdUnit4Net or keep thin enough to skip | Partial |
| GDScript scenes | Manual / GdUnit4 (GDScript) | Yes |

**Architectural insight:** The thicker PcpClient is and the thinner the bridge, the more we can test with plain xUnit outside Godot. This validates the three-layer architecture.

### Alternatives Considered
- **GDScript-only (no C#):** Loses xUnit testing, static typing, and clean library separation. Rejected.
- **GodotSharp NuGet in the library:** Would couple PcpClient to Godot. Rejected — library must be Godot-free.
- **Godot HttpRequest node:** Signal-based, Godot-coupled. Rejected for the library; acceptable if bridge needs Godot-native HTTP.

---

## R3: Binding Configuration Format

### Decision: TOML as the primary human-authored format

### Rationale
The binding model (flat list of node-to-metric mappings with normalisation ranges) fits TOML's sweet spot. TOML provides comment support, unambiguous spec, and zero footguns — ideal for "a file an SRE edits in vim at 2am."

### Format Evaluation Summary

| Criterion | TOML | YAML | JSON | .tres | Custom DSL |
|-----------|------|------|------|-------|------------|
| Human readability | Excellent | Very Good | Poor | Moderate | Controllable |
| .NET parsing | Tomlyn | YamlDotNet | Built-in | Godot-only | Build it |
| Godot native load | No | No | **Yes** | **Yes** | No |
| Comments support | Yes | Yes | **No** | No | Controllable |
| Cross-platform | Yes | Yes | Yes | No | Depends |

### TOML Example

```toml
[meta]
scene = "res://scenes/pmview_classic.tscn"
endpoint = "http://localhost:44322"
poll_interval_ms = 1000

[[bindings]]
scene_node = "CpuLoadBar"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.0, 5.0]

[[bindings]]
scene_node = "DiskIOSpinner"
metric = "disk.dev.read"
property = "rotation_speed"
source_range = [0.0, 1000.0]
target_range = [0.0, 360.0]
instance_filter = "sda"  # optional: specific instance
```

### Key Decisions
- **C# bridge parses TOML** using Tomlyn (.NET Standard 2.0+, actively maintained). Godot never touches the config directly.
- **JSON kept as fallback** for machine-generated configs or future Godot-side loading.
- **YAML rejected** — Norway problem, indentation ambiguity, spec complexity. Not worth the risk for a simple binding model.
- **.tres rejected** — locks config into Godot runtime, prevents xUnit testing of binding logic.
- **Custom DSL rejected** — YAGNI. Original pmview's DSL was necessary in 1997; we have better options now.

### Historical Context
Original pmview used a custom DSL (see `pmview(5)`) with whitespace-delimited, parenthesis-nested syntax mapping fixed primitives (`_bar`, `_stack`, `_grid`) to metrics with normalisation divisors. Tightly coupled to pmview's fixed visual vocabulary. pmview-nextgen's binding model is more flexible (arbitrary scene nodes, arbitrary properties).

---

## R4: PcpClient Workflow Pattern

### Decision: Explicit context management with polling loop

### Recommended Client Workflow

1. **Create context** — `GET /pmapi/context?polltimeout=60` → get context number
2. **Discover metrics** — `GET /pmapi/children?prefix=kernel` (tree walk) or `GET /pmapi/metric?prefix=kernel`
3. **Describe metrics** — `GET /pmapi/metric?names=kernel.all.load` → metadata, type, semantics, help text
4. **Enumerate instances** — `GET /pmapi/indom?name=kernel.all.load` → instance names and IDs
5. **Fetch values** — `GET /pmapi/fetch?names=kernel.all.load&context=N` on polling timer
6. **Historical queries** — `GET /series/query?expr=kernel.all.load[samples:100]` (requires Valkey)

### Context Lifecycle
- Context numbers are server-assigned integers
- Refreshed by any `/pmapi/*` call that includes the context number
- Expire after `polltimeout` seconds of inactivity
- Client should handle context expiry gracefully (re-create on 404/error)
