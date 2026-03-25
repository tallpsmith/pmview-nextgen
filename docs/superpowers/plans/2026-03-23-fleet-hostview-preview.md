# Fleet HostView Preview & Dive-In Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder bars in fleet focus mode with a real, live HostView preview that pre-fetches in the background, with a dive-in transition to the full HostView scene and ESC-back navigation.

**Architecture:** Decouple `RuntimeSceneBuilder` into `HostSceneBuilder` with `BuildZones()` (3D visualisation only) and `AddHostViewUi()` (UI panels). Fleet focus runs the loading pipeline in background, drives a matrix progress grid on the holographic beam, then fades in the real zone scene. Double-click/Enter dives into the full HostView; ESC returns to fleet.

**Tech Stack:** C# (.NET 8), GDScript, Godot 4.6, GLSL shaders

**Spec:** `docs/superpowers/specs/2026-03-23-fleet-hostview-preview-design.md`

---

## Chunk 1: HostSceneBuilder Rename & Split

### Task 1: Rename RuntimeSceneBuilder → HostSceneBuilder

**Files:**
- Rename: `src/pmview-app/scripts/RuntimeSceneBuilder.cs` → `src/pmview-app/scripts/HostSceneBuilder.cs`
- Modify: `src/pmview-app/scripts/LoadingPipeline.cs:102`
- Modify: `docs/ARCHITECTURE.md` (references to RuntimeSceneBuilder)

- [ ] **Step 1: Rename the file**

```bash
cd "src/pmview-app/scripts"
git mv RuntimeSceneBuilder.cs HostSceneBuilder.cs
```

- [ ] **Step 2: Rename the class and update internal references**

In `HostSceneBuilder.cs`:
- Line 14: `public static class RuntimeSceneBuilder` → `public static class HostSceneBuilder`
- Line 16: `"RuntimeSceneBuilder"` → `"HostSceneBuilder"`
- Line 392: `"[RuntimeSceneBuilder]"` → `"[HostSceneBuilder]"`
- Line 404: `"[RuntimeSceneBuilder]"` → `"[HostSceneBuilder]"`
- Line 416: `"[RuntimeSceneBuilder]"` → `"[HostSceneBuilder]"`

- [ ] **Step 3: Update LoadingPipeline.cs caller**

In `LoadingPipeline.cs` line 102:
```csharp
// Before:
BuiltScene = RuntimeSceneBuilder.Build(layout, endpoint, mode, hostnameOverride);
// After:
BuiltScene = HostSceneBuilder.Build(layout, endpoint, mode, hostnameOverride);
```

- [ ] **Step 4: Update ARCHITECTURE.md**

Replace all occurrences of `RuntimeSceneBuilder` with `HostSceneBuilder`.

- [ ] **Step 5: Build to verify rename compiles**

```bash
export PATH="/opt/homebrew/bin:$PATH"
dotnet build pmview-nextgen.ci.slnf
```
Expected: Build succeeds with no errors.

- [ ] **Step 6: Run tests**

```bash
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
```
Expected: All tests pass (rename is purely cosmetic).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Rename RuntimeSceneBuilder to HostSceneBuilder for clarity"
```

### Task 2: Split Build() into BuildZones() + AddHostViewUi()

**Files:**
- Modify: `src/pmview-app/scripts/HostSceneBuilder.cs`
- Modify: `src/pmview-app/scripts/LoadingPipeline.cs:102`

- [ ] **Step 1: Extract BuildZones() method**

In `HostSceneBuilder.cs`, add a new public method. `BuildZones()` creates the root Node3D (without the controller script), adds MetricPoller, SceneBinder, zones, and ambient labels — but NOT the UI layer:

```csharp
/// <summary>
/// Builds the 3D zone visualisation without UI panels or controller script.
/// Used by fleet preview (read-only) and as the first stage of a full build.
/// </summary>
public static Node3D BuildZones(SceneLayout layout, string pmproxyEndpoint,
    string mode = "live", string? hostnameOverride = null,
    IProgress<float>? progress = null)
{
    _log.LogInformation("BuildZones starting...");
    var root = new Node3D { Name = "HostViewZones" };
    var hostname = hostnameOverride ?? layout.Hostname;
    AddMetricPoller(root, pmproxyEndpoint, hostname);
    AddSceneBinder(root);

    _log.LogInformation("Building {ZoneCount} zones...", layout.Zones.Count);
    for (var i = 0; i < layout.Zones.Count; i++)
    {
        BuildZone(root, layout.Zones[i]);
        progress?.Report((float)(i + 1) / layout.Zones.Count);
    }

    BuildAmbientLabels(root);

    // Set Owner on all descendants so find_child works after reparenting.
    SetOwnerRecursive(root, root);

    _log.LogInformation("BuildZones complete. Root children: {ChildCount}", root.GetChildCount());
    return root;
}
```

Note: No controller script attached. Root is named "HostViewZones" not "HostView".

- [ ] **Step 2: Extract AddHostViewUi() method**

Add a new public method that grafts UI panels onto an existing zones scene and attaches the controller script:

```csharp
/// <summary>
/// Grafts the HostView UI layer (panels, help, detail) and controller script
/// onto a zones scene built by <see cref="BuildZones"/>.
/// Also re-sets ownership so find_child works after reparenting.
/// </summary>
public static void AddHostViewUi(Node3D zonesRoot, string mode = "live")
{
    _log.LogInformation("AddHostViewUi starting...");

    // Attach the controller script to the root
    var script = GD.Load<Script>(ControllerScriptPath);
    if (script == null)
        _log.LogError("FAILED to load controller script: {ScriptPath}", ControllerScriptPath);
    else
        zonesRoot.SetScript(script);

    // Rename to match what HostViewController expects
    zonesRoot.Name = "HostView";

    AddRangeTuningPanel(zonesRoot);

    if (mode == "archive")
        AddTimeControl(zonesRoot);

    // Re-set ownership after adding UI nodes
    SetOwnerRecursive(zonesRoot, zonesRoot);

    _log.LogInformation("AddHostViewUi complete.");
}
```

- [ ] **Step 3: Rewrite Build() to delegate**

Replace the existing `Build()` method body to call the two new methods:

```csharp
public static Node3D Build(SceneLayout layout, string pmproxyEndpoint,
    string mode = "live", string? hostnameOverride = null,
    IProgress<float>? progress = null)
{
    var root = BuildZones(layout, pmproxyEndpoint, mode, hostnameOverride, progress);
    AddHostViewUi(root, mode);
    return root;
}
```

Remove `CreateHostViewRoot()` — it's no longer needed. Its logic is split between `BuildZones()` (creates plain Node3D) and `AddHostViewUi()` (attaches controller script).

- [ ] **Step 4: Build to verify split compiles**

```bash
export PATH="/opt/homebrew/bin:$PATH"
dotnet build pmview-nextgen.ci.slnf
```
Expected: Build succeeds. `Build()` delegates to the two new methods, so existing callers (LoadingPipeline) are unchanged.

- [ ] **Step 5: Run tests to verify no behaviour change**

```bash
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
```
Expected: All tests pass. The refactor is behaviour-preserving — `Build()` produces the same output.

- [ ] **Step 6: Commit**

```bash
git add src/pmview-app/scripts/HostSceneBuilder.cs
git commit -m "Split HostSceneBuilder into BuildZones() + AddHostViewUi()

BuildZones returns zones-only scene graph (no UI, no controller script).
AddHostViewUi grafts panels and controller onto an existing zones root.
Build() now delegates to both — existing callers unchanged."
```

### Task 2b: Add zonesOnly mode to LoadingPipeline

**Files:**
- Modify: `src/pmview-app/scripts/LoadingPipeline.cs`

The LoadingPipeline currently always calls `HostSceneBuilder.Build()` which attaches the controller script and UI. When used for fleet preview, the controller script's `_ready()` would fire and call `SceneManager.go_to_main_menu()` (because `SceneManager.built_scene` is null). We need a flag to call `BuildZones()` instead.

- [ ] **Step 1: Add ZonesOnly property**

In `LoadingPipeline.cs`, add after the `MinPhaseDelayMs` export (line 37):

```csharp
/// <summary>
/// When true, builds zones only (no UI panels or controller script).
/// Used by fleet preview to get a read-only visualisation.
/// </summary>
[Export] public bool ZonesOnly { get; set; } = false;
```

- [ ] **Step 2: Branch on ZonesOnly in phase 5**

Replace line 102:
```csharp
// Before:
BuiltScene = HostSceneBuilder.Build(layout, endpoint, mode, hostnameOverride);
// After:
BuiltScene = ZonesOnly
    ? HostSceneBuilder.BuildZones(layout, endpoint, mode, hostnameOverride)
    : HostSceneBuilder.Build(layout, endpoint, mode, hostnameOverride);
```

- [ ] **Step 3: Build and test**

```bash
export PATH="/opt/homebrew/bin:$PATH"
dotnet build pmview-nextgen.ci.slnf
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
```
Expected: All pass. Default `ZonesOnly = false` preserves existing behaviour.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/LoadingPipeline.cs
git commit -m "Add ZonesOnly flag to LoadingPipeline for fleet preview

When true, calls BuildZones() instead of Build() to avoid
attaching controller script and UI panels."
```

## Chunk 2: SceneManager Navigation & ESC Hierarchy

### Task 3: Add fleet origin tracking to SceneManager

**Files:**
- Modify: `src/pmview-app/scripts/SceneManager.gd`

- [ ] **Step 1: Add new state variables and methods**

Add after the existing `built_scene` variable (line 10):

```gdscript
## Tracks where HostView was launched from ("fleet" or "")
var origin_scene: String = ""

## Hostname that was focused when dive-in occurred (for restoring fleet focus)
var fleet_focused_hostname: String = ""
```

Add new methods after `go_to_fleet_view()`:

```gdscript
func go_to_host_view_from_fleet(scene: Node3D, focused_hostname: String) -> void:
	origin_scene = "fleet"
	fleet_focused_hostname = focused_hostname
	built_scene = scene
	get_tree().change_scene_to_file("res://scenes/host_view.tscn")


func return_to_fleet() -> void:
	origin_scene = ""
	# connection_config and fleet_focused_hostname are preserved
	# so fleet view can restore focus on the same host
	if built_scene:
		built_scene.queue_free()
		built_scene = null
	get_tree().change_scene_to_file("res://scenes/fleet_view.tscn")
```

Also update `go_to_main_menu()` to clear fleet state:

```gdscript
func go_to_main_menu() -> void:
	connection_config = {}
	origin_scene = ""
	fleet_focused_hostname = ""
	if built_scene:
		built_scene.queue_free()
		built_scene = null
	get_tree().change_scene_to_file("res://scenes/main_menu.tscn")
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/scripts/SceneManager.gd
git commit -m "Add fleet origin tracking to SceneManager

New state: origin_scene, fleet_focused_hostname.
New methods: go_to_host_view_from_fleet(), return_to_fleet().
Enables ESC-back from HostView to fleet focus."
```

### Task 4: Update HostViewController ESC for fleet-origin navigation

**Files:**
- Modify: `src/pmview-app/scripts/HostViewController.gd`

- [ ] **Step 1: Replace the double-ESC handler with fleet-aware logic**

In `_unhandled_input()`, find the existing double-ESC block (around lines 239-248). Replace it with:

```gdscript
	# ESC — return to fleet if launched from fleet (single press)
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		if SceneManager.origin_scene == "fleet":
			SceneManager.return_to_fleet()
			return
		# Original double-ESC for main menu
		if _esc_pending:
			SceneManager.go_to_main_menu()
		else:
			_esc_pending = true
			esc_label.visible = true
			_esc_timer = get_tree().create_timer(2.0)
			_esc_timer.timeout.connect(_dismiss_esc)
		return
```

- [ ] **Step 2: Update help content for fleet-origin context**

In `_setup_help_content()`, update the "General" group to conditionally show the correct ESC behaviour:

```gdscript
	var esc_text: String
	if SceneManager.origin_scene == "fleet":
		esc_text = "Return to Fleet"
	else:
		esc_text = "Return to main menu"
	var general_group := HelpGroup.create("General", orange, [
		HelpGroup.HelpEntry.create("ESC", esc_text) if SceneManager.origin_scene == "fleet" \
			else HelpGroup.HelpEntry.create("ESC × 2", esc_text),
	])
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/HostViewController.gd
git commit -m "Single-ESC returns to fleet when HostView launched from fleet

Maintains double-ESC to main menu when launched from loading screen.
Help panel text updates to reflect fleet-origin context."
```

## Chunk 3: Matrix Progress Grid

### Task 5: Write the matrix progress grid shader

**Files:**
- Create: `src/pmview-app/addons/pmview-bridge/building_blocks/matrix_progress_grid.gdshader`

- [ ] **Step 1: Create the shader**

The shader renders a 10×10 grid of cells on a plane. Each cell can be off (transparent dark) or on (holographic cyan with glow). Cell states are passed via a uniform array.

```glsl
shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled;

uniform float cell_states[100]; // 0.0 = off, 0.0..1.0 = glow intensity
uniform vec3 on_colour : source_color = vec3(0.024, 0.714, 0.831); // holographic cyan
uniform vec3 off_colour : source_color = vec3(0.067, 0.067, 0.067);
uniform float global_alpha : hint_range(0.0, 1.0) = 1.0;
uniform float grid_gap : hint_range(0.0, 0.1) = 0.04; // gap between cells as fraction of cell

void fragment() {
    // Map UV to grid coordinates
    vec2 cell_uv = UV * 10.0;
    ivec2 cell_id = ivec2(floor(cell_uv));
    vec2 local_uv = fract(cell_uv);

    // Clamp cell index
    cell_id = clamp(cell_id, ivec2(0), ivec2(9));
    int idx = cell_id.y * 10 + cell_id.x;

    // Grid gap — transparent between cells
    float gap_mask = step(grid_gap, local_uv.x) * step(local_uv.x, 1.0 - grid_gap)
                   * step(grid_gap, local_uv.y) * step(local_uv.y, 1.0 - grid_gap);

    float state = cell_states[idx];
    vec3 colour = mix(off_colour, on_colour, step(0.01, state));

    // Glow pulse: state > 1.0 means freshly activated, add bloom
    float glow = max(state - 1.0, 0.0);
    colour += on_colour * glow * 2.0;

    // Slight opacity variation for active cells
    float cell_alpha = mix(0.15, mix(0.7, 1.0, fract(float(idx) * 0.618)), step(0.01, state));

    ALBEDO = colour;
    ALPHA = gap_mask * cell_alpha * global_alpha;
}
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/addons/pmview-bridge/building_blocks/matrix_progress_grid.gdshader
git commit -m "Add matrix progress grid shader for fleet loading animation

10x10 cell grid with on/off states, holographic cyan glow pulses,
and gap rendering between cells."
```

### Task 6: Write the matrix progress grid GDScript

**Files:**
- Create: `src/pmview-app/addons/pmview-bridge/building_blocks/matrix_progress_grid.gd`

- [ ] **Step 1: Create the script**

```gdscript
# matrix_progress_grid.gd
# 10×10 cell grid that fills with random scatter to visualise loading progress.
# Sits on the top surface of a holographic beam.
extends MeshInstance3D

signal progress_complete

## Width of the grid plane (matches beam ceiling width)
@export var grid_width: float = 18.0
## Depth of the grid plane (matches beam ceiling depth)
@export var grid_depth: float = 10.0

const GRID_SIZE := 10
const TOTAL_CELLS := GRID_SIZE * GRID_SIZE
const GLOW_DURATION := 0.3  # seconds for glow pulse to fade

var _cell_states: Array[float] = []  # current visual state per cell
var _activated: Array[bool] = []      # which cells have been activated
var _scatter_order: Array[int] = []   # pre-shuffled activation order
var _active_count: int = 0
var _glow_timers: Array[float] = []   # remaining glow time per cell
var _material: ShaderMaterial = null
var _dissolving: bool = false


func _ready() -> void:
	# Build the plane mesh
	var plane := PlaneMesh.new()
	plane.size = Vector2(grid_width, grid_depth)
	mesh = plane

	# Create shader material
	_material = ShaderMaterial.new()
	_material.shader = preload("res://addons/pmview-bridge/building_blocks/matrix_progress_grid.gdshader")
	set_surface_override_material(0, _material)

	# Initialise cell state arrays
	_cell_states.resize(TOTAL_CELLS)
	_activated.resize(TOTAL_CELLS)
	_glow_timers.resize(TOTAL_CELLS)
	for i in range(TOTAL_CELLS):
		_cell_states[i] = 0.0
		_activated[i] = false
		_glow_timers[i] = 0.0

	# Pre-shuffle activation order
	_scatter_order = []
	for i in range(TOTAL_CELLS):
		_scatter_order.append(i)
	_scatter_order.shuffle()

	_push_states_to_shader()


func _process(delta: float) -> void:
	var needs_update := false
	for i in range(TOTAL_CELLS):
		if _glow_timers[i] > 0.0:
			_glow_timers[i] = maxf(_glow_timers[i] - delta, 0.0)
			# State: 1.0 = on, >1.0 = glowing (shader reads glow from state - 1.0)
			var glow_intensity := _glow_timers[i] / GLOW_DURATION
			_cell_states[i] = 1.0 + glow_intensity
			needs_update = true
		elif _activated[i] and _cell_states[i] != 1.0:
			_cell_states[i] = 1.0
			needs_update = true

	if needs_update:
		_push_states_to_shader()


## Set loading progress (0.0 to 1.0). Activates cells in scatter order.
func set_progress(progress: float) -> void:
	var target_count := clampi(roundi(progress * TOTAL_CELLS), 0, TOTAL_CELLS)
	while _active_count < target_count:
		var cell_idx: int = _scatter_order[_active_count]
		_activated[cell_idx] = true
		_glow_timers[cell_idx] = GLOW_DURATION
		_cell_states[cell_idx] = 1.0 + 1.0  # start with full glow
		_active_count += 1

	_push_states_to_shader()

	if _active_count >= TOTAL_CELLS and not _dissolving:
		_dissolving = true
		progress_complete.emit()


## Dissolve the grid (fade out all cells)
func dissolve(duration: float = 0.5) -> void:
	var tween := create_tween()
	tween.tween_method(_set_global_alpha, 1.0, 0.0, duration)
	tween.tween_callback(queue_free)


func _set_global_alpha(val: float) -> void:
	if _material:
		_material.set_shader_parameter("global_alpha", val)


func _push_states_to_shader() -> void:
	if not _material:
		return
	# Godot shader arrays are set as PackedFloat32Array
	var packed := PackedFloat32Array()
	packed.resize(TOTAL_CELLS)
	for i in range(TOTAL_CELLS):
		packed[i] = _cell_states[i]
	_material.set_shader_parameter("cell_states", packed)
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/addons/pmview-bridge/building_blocks/matrix_progress_grid.gd
git commit -m "Add matrix progress grid script for fleet loading animation

Random scatter cell activation driven by set_progress(0-1),
glow pulse on activation, dissolve on completion."
```

## Chunk 4: Fleet Host Pipeline

### Task 7: Create FleetHostPipeline coordinator

**Files:**
- Create: `src/pmview-app/scripts/fleet_host_pipeline.gd`

- [ ] **Step 1: Create the pipeline coordinator**

This GDScript node instantiates `LoadingPipeline` (C#), connects its signals, and drives the matrix grid progress. It runs the pipeline for a single host within the fleet scene.

```gdscript
# fleet_host_pipeline.gd
# Coordinates the LoadingPipeline for a single host within the fleet view.
# Drives a matrix progress grid and holds the built zones scene on completion.
extends Node

signal build_completed(zones_root: Node3D)
signal build_failed(error: String)

var _pipeline: Node = null
var _matrix_grid: MeshInstance3D = null
var _zones_root: Node3D = null
var _is_complete: bool = false

## Cell allocation per phase (must sum to 100)
const PHASE_CELLS := {0: 10, 1: 20, 2: 0, 3: 10, 4: 20, 5: 40}
var _cells_so_far: int = 0


func start(endpoint: String, mode: String, hostname: String,
		matrix_grid: MeshInstance3D) -> void:
	_matrix_grid = matrix_grid

	# Instantiate the C# LoadingPipeline node
	var pipeline_script := load("res://scripts/LoadingPipeline.cs")
	if pipeline_script == null:
		push_error("[FleetHostPipeline] Failed to load LoadingPipeline script")
		build_failed.emit("Failed to load pipeline")
		return

	_pipeline = Node.new()
	_pipeline.name = "FleetLoadingPipeline"
	_pipeline.set_script(pipeline_script)
	# Re-fetch managed wrapper after SetScript
	_pipeline = instance_from_id(_pipeline.get_instance_id())

	# No artificial delay — let it rip, the matrix animation is the visual feedback
	_pipeline.set("MinPhaseDelayMs", 0)
	# Build zones only — no UI panels or controller script
	_pipeline.set("ZonesOnly", true)

	add_child(_pipeline)

	_pipeline.PhaseCompleted.connect(_on_phase_completed)
	_pipeline.PipelineCompleted.connect(_on_pipeline_completed)
	_pipeline.PipelineError.connect(_on_pipeline_error)

	_pipeline.StartPipeline(endpoint, mode, hostname, "", false)


func _on_phase_completed(phase_index: int, _phase_name: String) -> void:
	var cells: int = PHASE_CELLS.get(phase_index, 0)
	_cells_so_far += cells
	if _matrix_grid and _matrix_grid.has_method("set_progress"):
		_matrix_grid.set_progress(float(_cells_so_far) / 100.0)


func _on_pipeline_completed() -> void:
	_is_complete = true
	_zones_root = _pipeline.get("BuiltScene") as Node3D
	# Pipeline was started with ZonesOnly=true, so _zones_root has no
	# controller script or UI layer — safe to add to the fleet scene tree.

	if _matrix_grid and _matrix_grid.has_method("set_progress"):
		_matrix_grid.set_progress(1.0)

	build_completed.emit(_zones_root)


func _on_pipeline_error(_phase_index: int, error: String) -> void:
	push_error("[FleetHostPipeline] Pipeline failed: %s" % error)
	build_failed.emit(error)


## Returns the built zones root, or null if not yet complete.
func get_zones_root() -> Node3D:
	return _zones_root


## Returns true if the pipeline has completed successfully.
func is_complete() -> bool:
	return _is_complete


## Clean up the pipeline node.
func cancel() -> void:
	if _pipeline:
		_pipeline.queue_free()
		_pipeline = null
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/scripts/fleet_host_pipeline.gd
git commit -m "Add FleetHostPipeline coordinator for fleet preview loading

Instantiates LoadingPipeline for single host, drives matrix grid
progress, holds built zones scene for dive-in handoff."
```

## Chunk 5: Fleet View Integration

### Task 8: Replace mock detail view with real HostView preview

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd`

This is the largest task. The FleetViewController needs to:
- Spawn a matrix grid on the beam top instead of placeholder bars
- Start the FleetHostPipeline when entering focus
- Fade in the zones scene when the pipeline completes
- Support dive-in (double-click / Enter) and clean up on exit

- [ ] **Step 1: Add new preloads and state variables**

At the top of `FleetViewController.gd`, add:

```gdscript
const FleetHostPipelineScript := preload("res://scripts/fleet_host_pipeline.gd")
const MatrixGridScript := preload("res://addons/pmview-bridge/building_blocks/matrix_progress_grid.gd")

# ... existing state vars ...

var _fleet_pipeline: Node = null
var _matrix_grid: MeshInstance3D = null
var _preview_zones: Node3D = null
var _preview_ready: bool = false
```

- [ ] **Step 2: Replace _spawn_mock_detail_view() with _start_preview_pipeline()**

Remove the existing `_spawn_mock_detail_view()` method (lines 274-285). Add:

```gdscript
func _start_preview_pipeline(host: Node3D) -> void:
	var config: Dictionary = SceneManager.connection_config
	var endpoint: String = config.get("endpoint", "http://localhost:54322")
	var mode: String = config.get("mode", "live")
	var hostname: String = host.hostname

	# Spawn matrix grid on beam top
	_matrix_grid = MeshInstance3D.new()
	_matrix_grid.set_script(MatrixGridScript)
	_matrix_grid.name = "MatrixProgressGrid"
	# Position at beam top (host position + beam height + small offset)
	_matrix_grid.position = host.position + Vector3(0, DETAIL_VIEW_HEIGHT + 0.1, 0)
	add_child(_matrix_grid)

	# Start the pipeline
	_fleet_pipeline = Node.new()
	_fleet_pipeline.set_script(FleetHostPipelineScript)
	_fleet_pipeline.name = "FleetHostPipeline"
	add_child(_fleet_pipeline)

	_fleet_pipeline.build_completed.connect(_on_preview_build_completed)
	_fleet_pipeline.build_failed.connect(_on_preview_build_failed)
	_fleet_pipeline.start(endpoint, mode, hostname, _matrix_grid)
```

- [ ] **Step 3: Add preview completion handlers**

```gdscript
func _on_preview_build_completed(zones_root: Node3D) -> void:
	_preview_zones = zones_root
	_preview_ready = true

	# Dissolve the matrix grid
	if _matrix_grid and _matrix_grid.has_method("dissolve"):
		_matrix_grid.dissolve(0.5)

	# Position zones above the host and add to scene
	if _preview_zones and _focused_host_index >= 0:
		var host: Node3D = _hosts[_focused_host_index]
		_preview_zones.position = host.position + Vector3(0, DETAIL_VIEW_HEIGHT, 0)
		add_child(_preview_zones)

		# Fade in: start transparent, tween to opaque
		# Use a modulate tween on each visible child
		_preview_zones.visible = true


func _on_preview_build_failed(error: String) -> void:
	push_warning("[FleetView] Preview build failed: %s" % error)
	# Clean up matrix grid — show error state or just remove
	if _matrix_grid:
		_matrix_grid.queue_free()
		_matrix_grid = null
```

- [ ] **Step 4: Update _enter_focus() to use the pipeline**

At the top of `_enter_focus()`, add a re-entry guard to cancel any existing pipeline if the user clicks a different host during a transition:

```gdscript
func _enter_focus(host_index: int) -> void:
	# Cancel any existing preview/pipeline if re-entering focus
	if _fleet_pipeline or _preview_zones:
		_cleanup_focus_state()
	_focused_host_index = host_index
	# ... rest of existing code ...
```

Then replace the call to `_spawn_mock_detail_view(host)` (line 202) with:

```gdscript
	_start_preview_pipeline(host)
```

Remove the line `_spawn_mock_detail_view(host)` entirely.

- [ ] **Step 5: Update _exit_focus() to clean up preview state**

In `_exit_focus()`, after the existing cleanup of `_detail_view` and `_beam`, add cleanup for the new state:

```gdscript
	# Clean up preview
	if _preview_zones:
		_preview_zones.queue_free()
		_preview_zones = null
	if _fleet_pipeline:
		if _fleet_pipeline.has_method("cancel"):
			_fleet_pipeline.cancel()
		_fleet_pipeline.queue_free()
		_fleet_pipeline = null
	if _matrix_grid:
		_matrix_grid.queue_free()
		_matrix_grid = null
	_preview_ready = false
```

Also remove the cleanup of `_detail_view` (it no longer exists):
```gdscript
	# Remove these lines:
	# if _detail_view:
	# 	_detail_view.queue_free()
	# 	_detail_view = null
```

- [ ] **Step 6: Add dive-in input handling**

In `_unhandled_input()`, add handling for double-click and Enter in focus mode:

```gdscript
	# Dive-in: double-click or Enter in focus mode with preview ready
	if _view_mode == ViewMode.FOCUS and _preview_ready:
		if event.is_action_pressed("ui_accept"):
			_dive_into_host_view()
			return
		if event is InputEventMouseButton and event.pressed \
				and event.button_index == MOUSE_BUTTON_LEFT and event.double_click:
			_dive_into_host_view()
			return
```

- [ ] **Step 7: Implement _dive_into_host_view()**

```gdscript
func _dive_into_host_view() -> void:
	if not _preview_zones or _focused_host_index < 0:
		return

	var host: Node3D = _hosts[_focused_host_index]
	var hostname: String = host.hostname

	# Detach the zones from our scene tree (don't free — we're handing it off)
	remove_child(_preview_zones)
	var zones: Node3D = _preview_zones
	_preview_zones = null

	# Graft the UI layer and controller script onto the zones root.
	# This transforms it from a read-only preview into a full HostView scene.
	var config: Dictionary = SceneManager.connection_config
	var mode: String = config.get("mode", "live")
	HostSceneBuilder.AddHostViewUi(zones, mode)

	# Clean up fleet state (beam, pipeline, grid) without freeing the zones
	_cleanup_focus_state()

	# Hand off to SceneManager
	SceneManager.go_to_host_view_from_fleet(zones, hostname)
```

- [ ] **Step 8: Extract _cleanup_focus_state() helper**

Factor out the cleanup logic used by both `_exit_focus()` and `_dive_into_host_view()`:

```gdscript
func _cleanup_focus_state() -> void:
	if _beam:
		_beam.queue_free()
		_beam = null
	if _fleet_pipeline:
		if _fleet_pipeline.has_method("cancel"):
			_fleet_pipeline.cancel()
		_fleet_pipeline.queue_free()
		_fleet_pipeline = null
	if _matrix_grid:
		_matrix_grid.queue_free()
		_matrix_grid = null
	if _preview_zones:
		_preview_zones.queue_free()
		_preview_zones = null
	_preview_ready = false

	# Restore all host opacities
	for host: Node3D in _hosts:
		host.set_opacity(1.0)

	_focused_host_index = -1
```

Update `_exit_focus()` to use this helper instead of duplicating cleanup.

- [ ] **Step 9: Add fleet focus restoration on _ready()**

At the end of `_ready()`, add auto-focus logic for return-from-host-view:

```gdscript
	# Restore focus if returning from HostView dive-in
	var restore_hostname: String = SceneManager.fleet_focused_hostname
	if not restore_hostname.is_empty():
		SceneManager.fleet_focused_hostname = ""
		# Find the host index by hostname
		for i in range(_hosts.size()):
			if _hosts[i].hostname == restore_hostname:
				# Defer to next frame so the scene is fully ready
				_enter_focus.call_deferred(i)
				break
```

- [ ] **Step 10: Remove _detail_view state variable and _spawn_mock_detail_view()**

Remove:
- `var _detail_view: Node3D = null` (line 30)
- The entire `_spawn_mock_detail_view()` method (lines 274-285)
- Any remaining references to `_detail_view` in `_exit_focus()`

- [ ] **Step 11: Build to verify compilation**

```bash
export PATH="/opt/homebrew/bin:$PATH"
dotnet build pmview-nextgen.ci.slnf
```
Expected: Build succeeds.

- [ ] **Step 12: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd
git commit -m "Replace mock detail view with real HostView preview pipeline

Fleet focus now runs LoadingPipeline in background, drives matrix
grid animation, fades in real zones on completion. Double-click or
Enter dives into full HostView; ESC returns to fleet focus."
```

## Chunk 6: Documentation & Cleanup

### Task 9: Update documentation

**Files:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `README.md` (if fleet view features are described)

- [ ] **Step 1: Update ARCHITECTURE.md**

Add a section describing the fleet preview pipeline and the HostSceneBuilder split. Update the existing RuntimeSceneBuilder references.

- [ ] **Step 2: Commit**

```bash
git add docs/ARCHITECTURE.md
git commit -m "Update architecture docs for HostSceneBuilder split and fleet preview"
```

### Task 10: End-to-end manual test checklist

This task cannot be automated — it requires the Godot editor and a running dev-environment stack.

- [ ] **Manual test 1: Fleet view loads, hosts display**
- [ ] **Manual test 2: Click host → camera flies, beam appears, matrix grid animates**
- [ ] **Manual test 3: Pipeline completes → matrix dissolves, zones fade in**
- [ ] **Manual test 4: Preview shows live metric data (bars moving)**
- [ ] **Manual test 5: Enter → transitions to full HostView**
- [ ] **Manual test 6: Double-click on preview → transitions to full HostView**
- [ ] **Manual test 7: ESC from HostView (fleet origin) → returns to fleet focus on same host**
- [ ] **Manual test 8: ESC from fleet focus → returns to patrol**
- [ ] **Manual test 9: ESC ESC from patrol → returns to main menu**
- [ ] **Manual test 10: Loading from main menu → HostView → ESC ESC → main menu (existing flow unchanged)**
- [ ] **Manual test 11: Archive mode preserves playback position through dive-in/return**
