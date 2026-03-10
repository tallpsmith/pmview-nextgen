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
| Can create Node3D scene trees | Must-have | _TBD_ | |
| Can set mesh types and materials | Must-have | _TBD_ | |
| Can set node properties (scale, position, colour) | Must-have | _TBD_ | |
| Works with Godot 4.4+ | Must-have | _TBD_ | |
| Stable enough for dev workflow | Should-have | _TBD_ | |
| Can create complete scenes from text description | Nice-to-have | _TBD_ | |
| Scene output compatible with binding config model | Must-have | _TBD_ | |

## Test Scenarios

### Scenario 1: Basic Bar Chart Scene
Prompt: "Create a 3D scene with 4 vertical bars named CpuBar1-4, each a BoxMesh with different heights"
- Expected: A scene file with 4 MeshInstance3D nodes
- Actual: _TBD_

### Scenario 2: Metric Dashboard Layout
Prompt: "Create a monitoring dashboard with a 2x2 grid of 3D bar groups, camera positioned for overview"
- Expected: Organised node hierarchy, appropriate camera
- Actual: _TBD_

### Scenario 3: Binding-Compatible Scene
Prompt: "Create a scene where node names match these binding paths: CpuBars/LoadBar, CpuBars/UserBar, DiskPanel/ReadSpinner"
- Expected: Correct node hierarchy matching binding config expectations
- Actual: _TBD_

## Setup Instructions

1. Install godot-mcp: _TBD — check https://github.com/search for godot-mcp_
2. Configure as MCP server in Claude Desktop or Claude Code
3. Open Godot project
4. Run evaluation scenarios

## Findings

_To be filled in after running the evaluation._

### Capabilities Confirmed
- _TBD_

### Limitations Discovered
- _TBD_

### Recommendation
- _TBD: Adopt / Adapt / Defer / Reject_

## Impact on Architecture

If godot-mcp works well:
- Future "AI World Creator" feature (spec FR-future) becomes viable
- Scene creation workflow shifts from manual Godot editor to AI-assisted
- Binding configs could potentially be auto-generated alongside scenes

If godot-mcp doesn't work:
- Scenes remain manually authored in Godot editor (perfectly fine for MVP)
- AI World Creator deferred to later investigation
- No impact on core architecture
