# Addon Packaging & Projector Install Design

## Goal

Make the `pmview-bridge` addon fully self-contained under `addons/pmview-bridge/` so it can be installed into any Godot project by copying a single directory. Add `--install-addon` to the host projector CLI to automate this.

## Current Problem

Generated `.tscn` scenes reference resources at three different locations:
- `res://addons/pmview-bridge/PcpBindable.cs` (addon)
- `res://scenes/building_blocks/grounded_bar.tscn` (project scenes)
- `res://scripts/building_blocks/grid_layout_3d.gd` (project scripts)

The building blocks are addon infrastructure but live outside the addon directory. This means you can't install the addon by copying one directory — you'd need to copy three.

## Design

### 1. Move building blocks into the addon

Move all building block files into `addons/pmview-bridge/building_blocks/`:

```
addons/pmview-bridge/
├── plugin.cfg
├── PcpBindable.cs
├── PcpBindingResource.cs
├── PcpBindingInspectorPlugin.cs
├── PmviewBridgePlugin.cs
├── MetricPoller.cs
├── MetricBrowserDialog.cs
├── SceneBinder.cs
├── *.cs.uid                          # Godot UID files (preserved)
├── building_blocks/
│   ├── grounded_shape.gd
│   ├── grounded_bar.tscn
│   ├── grounded_cylinder.tscn
│   ├── grid_layout_3d.gd
│   └── zone_label.tscn
```

**Path updates required:**

| File | Old path | New path |
|------|----------|----------|
| `grounded_shape.gd` | `res://scripts/building_blocks/` | `res://addons/pmview-bridge/building_blocks/` |
| `grounded_bar.tscn` | `res://scenes/building_blocks/` | `res://addons/pmview-bridge/building_blocks/` |
| `grounded_cylinder.tscn` | `res://scenes/building_blocks/` | `res://addons/pmview-bridge/building_blocks/` |
| `grid_layout_3d.gd` | `res://scripts/building_blocks/` | `res://addons/pmview-bridge/building_blocks/` |
| `zone_label.tscn` | `res://scenes/building_blocks/` | `res://addons/pmview-bridge/building_blocks/` |

**Files that reference these paths (need updating):**
- `TscnWriter.cs` — ext_resource paths for bar_scene, cylinder_scene, grid_script
- `grounded_bar.tscn` — ext_resource for grounded_shape.gd
- `grounded_cylinder.tscn` — ext_resource for grounded_shape.gd
- TscnWriter tests — path assertions

**Delete after move:**
- `godot-project/scenes/building_blocks/` (empty)
- `godot-project/scripts/building_blocks/` (empty)

### 2. Add `--install-addon` to projector CLI

```bash
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  --install-addon \
  -o /path/to/my-godot-project/scenes/host_view.tscn
```

**Behaviour:**
1. Infer Godot project root from output path — walk parent directories looking for `project.godot`
2. If not found, error with clear message
3. Find addon source: walk from the projector's assembly directory up to repo root, then `godot-project/addons/pmview-bridge/`
4. Copy `addons/pmview-bridge/` directory into `<godot-root>/addons/pmview-bridge/`, overwriting existing files (idempotent)
5. Proceed with normal scene generation

**Without `--install-addon`:** no copying, assumes addon is already installed.

**Error cases:**
- Output path has no `project.godot` ancestor → error: "Cannot find Godot project root. Ensure output path is inside a Godot project."
- Addon source directory not found → error: "Cannot find addon source. Run from the pmview-nextgen repository."

### 3. Future: embedded resources (#22)

When the projector is published as a standalone binary, embed the addon files as .NET assembly resources so it doesn't need the source repo. Tracked in GitHub issue #22.
