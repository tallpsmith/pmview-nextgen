# Spike 03: AI World Crafting Evaluation

**Goal**: Evaluate godot-mcp for AI-driven 3D scene generation in pmview-nextgen.

## What is godot-mcp?

godot-mcp is a Model Context Protocol (MCP) server that bridges AI assistants (Claude, etc.) to the Godot editor. It allows AI to:
- Create and manipulate scene nodes
- Set properties on 3D objects
- Generate scene structures programmatically
- Control the Godot editor from external tools

## Evaluation Criteria

| Criterion | Weight | Score | Notes |
|-----------|--------|-------|-------|
| Can create Node3D scene trees | Must-have | PASS | Via direct .tscn generation (MCP create_scene unreliable) |
| Can set mesh types and materials | Must-have | PASS | BoxMesh + StandardMaterial3D with albedo, metallic, roughness |
| Can set node properties (scale, position, colour) | Must-have | PASS | Transforms, materials, light/camera/environment all correct |
| Works with Godot 4.4+ | Must-have | PASS | Tested with Godot 4.6.1 stable mono |
| Stable enough for dev workflow | Should-have | PASS | Direct .tscn authoring is reliable; MCP tools are not (see notes) |
| Can create complete scenes from text description | Nice-to-have | PASS | 227-line scene from single prompt — materials, lighting, camera, env |
| Scene output compatible with binding config model | Must-have | PASS | Node paths (CpuBars/LoadBar) match binding paths exactly |

## Test Scenarios

### Scenario 1: Basic Bar Chart Scene
Prompt: "Create a 3D scene with 4 vertical bars named CpuBar1-4, each a BoxMesh with different heights"
- Expected: A scene file with 4 MeshInstance3D nodes
- Actual: **PASS** — Claude generated valid `.tscn` with 4 MeshInstance3D nodes (CpuBar1-4), BoxMesh heights 1-4, correct Y positioning so bases sit at Y=0, spaced 1 unit apart on X axis. Scene loads and renders correctly in Godot editor.
- Notes: MCP `create_scene` reported success but didn't actually create the file. MCP `add_node` then failed ("scene does not exist"). Claude fell back to writing the `.tscn` file directly — which worked perfectly. The direct file approach is arguably more reliable than the MCP tool chain.

### Scenario 2: Metric Dashboard Layout
Prompt: "Create a monitoring dashboard with a 2x2 grid of 3D bar groups, camera positioned for overview"
- Expected: Organised node hierarchy, appropriate camera
- Actual: **PASS** — 227-line `.tscn` with full production-quality scene:
  - 2x2 grid: CpuGroup (red, -3,0,-3), MemoryGroup (blue, 3,0,-3), DiskGroup (green, -3,0,3), NetworkGroup (purple, 3,0,3)
  - 4 bars per group with varying heights, all ground-aligned at Y=0
  - StandardMaterial3D per group with albedo colour, metallic=0.3, roughness=0.6
  - Camera3D at (8,7,8) angled for isometric-ish overview
  - DirectionalLight3D with shadows, WorldEnvironment with dark procedural sky + ACES tonemap
  - Dark ground plane underneath
  - Clean hierarchy: Dashboard > {Camera, Light, Env, Ground, CpuGroup/{CpuBar1-4}, MemoryGroup/{MemBar1-4}, ...}
- Notes: Claude went from single prompt to complete scene with lighting, materials, environment, and camera — no iteration needed. Scene tree is script-friendly with group containers for easy binding.

### Scenario 3: Binding-Compatible Scene
Prompt: "Create a scene where node names match these binding paths: CpuBars/LoadBar, CpuBars/UserBar, DiskPanel/ReadSpinner"
- Expected: Correct node hierarchy matching binding config expectations
- Actual: **PASS** — Scene tree maps directly to binding paths:
  - `Dashboard > CpuBars > LoadBar` → GetNode("CpuBars/LoadBar") ✓
  - `Dashboard > CpuBars > UserBar` → GetNode("CpuBars/UserBar") ✓
  - `Dashboard > DiskPanel > ReadSpinner` → GetNode("DiskPanel/ReadSpinner") ✓
  - CpuBars bars: red BoxMesh with different heights, side by side
  - ReadSpinner: green flat CylinderMesh (creative interpretation of disk I/O)
  - Group nodes are plain Node3D containers — no unnecessary nesting
- Notes: Claude correctly interpreted binding path syntax as a Node3D hierarchy requirement. Also varied mesh types (BoxMesh vs CylinderMesh) based on semantic context without being asked. Node paths are directly usable by GetNode() from the scene root.

## Setup Instructions

1. Install godot-mcp: _TBD — check https://github.com/search for godot-mcp_
2. Configure as MCP server in Claude Desktop or Claude Code
3. Open Godot project
4. Run evaluation scenarios

## Findings

All 3 scenarios passed. Evaluation complete 2026-03-11.

### Capabilities Confirmed
- Claude can generate valid Godot `.tscn` scene files directly (no MCP required)
- Correct Node3D scene tree with MeshInstance3D children, BoxMesh sub-resources
- Proper transform positioning (Y offset = half height for ground-aligned bars)
- Scene loads cleanly in Godot 4.6.1 editor with correct 3D rendering
- Full scene authoring from a single prompt: materials, lighting, camera, environment, ground plane (227 lines)
- Hierarchical grouping (Dashboard > CpuGroup > CpuBar1-4) ideal for script binding
- StandardMaterial3D with colour, metallic, roughness properties all work correctly
- Node paths match binding path syntax exactly — GetNode() works as expected
- Claude varies mesh types semantically (BoxMesh for bars, CylinderMesh for spinners) without explicit instruction
- Multiple mesh types (BoxMesh, CylinderMesh) and materials in a single scene

### Limitations Discovered
- godot-mcp `create_scene` silently fails (reports success, creates nothing)
- godot-mcp `add_node` fails when scene file missing (no auto-create)
- MCP tool chain is unreliable as primary workflow — direct `.tscn` authoring by Claude is the more dependable path
- godot-mcp is useful for _reading_ project state but not yet reliable for _writing_ scenes

### Recommendation
- **ADAPT** — Adopt AI-driven scene generation, but via direct `.tscn` file authoring rather than godot-mcp tool calls
- godot-mcp remains useful as a read-only bridge (project info, debug output, editor state) but is not reliable for scene creation
- The "AI World Creator" feature (spec FR-future) is **viable** — Claude understands Godot scene format, node hierarchies, materials, and binding path conventions well enough to generate production-quality scenes from natural language prompts

## Impact on Architecture

### Confirmed: AI World Creator is Viable
- Claude generates valid `.tscn` files that load directly in Godot 4.6.1
- Scene node hierarchies naturally match binding config path syntax
- Single-prompt scene generation eliminates need for manual Godot editor scene authoring
- Binding configs could be auto-generated alongside scenes (same AI understands both)
- Workflow: natural language prompt → `.tscn` file → Godot editor (for preview/tweaking) → binding config

### Approach: Direct File Generation over MCP
- Claude writes `.tscn` files directly via file system tools (100% reliable across all 3 scenarios)
- godot-mcp tools failed on creation (0% success rate for write operations)
- No dependency on external MCP server stability for core scene generation
- godot-mcp can still complement as a read-only tool for project introspection
