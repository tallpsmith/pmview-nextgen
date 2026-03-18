# Range Tuning Panel v2 Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the v1 slider-based range tuning panel with a preset-button modal, add a persistent nano-style HUD bar, and add camera auto-focus when selecting zone presets.

**Architecture:** The HUD bar and tuning modal are separate GDScript scenes in the addon's `ui/` directory. The modal calls existing `SceneBinder` C# methods (`UpdateSourceRangeMax`, `GetSourceRanges`) plus a new `GetZoneCentroid` method. The camera gains a `focus_on_position()` method for smooth panning. F1 toggles the modal via `_unhandled_input()`. SharedZones defaults are aligned to match preset values.

**Tech Stack:** C# (.NET 8.0) for SceneBinder additions, GDScript for UI panel/HUD/camera, Godot 4.6 scene format for .tscn files, xUnit for C# tests.

**Spec:** `docs/superpowers/specs/2026-03-18-range-tuning-panel-v2-design.md`

---

## Chunk 1: Foundation — SharedZones Defaults, GetZoneCentroid API, Camera Focus

Bottom-up: fix the defaults, add the new C# API, then add camera focus. All pure logic — no UI yet.

### Task 1: Align SharedZones Defaults to Preset Values

**Files:**
- Modify: `src/pmview-projection-core/src/PmviewProjectionCore/Profiles/SharedZones.cs:57-58,81-82`
- Test: `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/SharedZonesTests.cs`

- [ ] **Step 1: Write failing tests for the new default values**

Add to `SharedZonesTests.cs`:

```csharp
[Theory]
[InlineData("Disk", 550_000_000f)]
[InlineData("Per-Disk", 550_000_000f)]
[InlineData("Network In", 125_000_000f)]
[InlineData("Network Out", 125_000_000f)]
public void Zone_SourceRangeMax_MatchesPreset(string zoneName, float expectedMax)
{
    var metrics = SharedZones.GetMetricNames(zoneName);
    Assert.NotEmpty(metrics);

    // Resolve first bytes-throughput metric in the zone to check its SourceRangeMax
    var allZones = typeof(SharedZones)
        .GetField("AllZones", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .GetValue(null) as PmviewProjectionCore.Models.ZoneDefinition[];

    var zone = allZones!.First(z => z.Name == zoneName);
    var bytesMetric = zone.Metrics.FirstOrDefault(m => m.MetricName.Contains("bytes"));
    if (bytesMetric != null)
        Assert.Equal(expectedMax, bytesMetric.SourceRangeMax);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/pmview-projection-core/tests/PmviewProjectionCore.Tests/ --filter "SourceRangeMax_MatchesPreset"`
Expected: FAIL — Disk Total has 500,000,000 and Per-Disk has 500,000.

- [ ] **Step 3: Update SharedZones defaults**

In `SharedZones.cs`, update `DiskTotalsZone()` (lines 57-58):

```csharp
new("disk.all.read_bytes",  ShapeType.Cylinder, "Read",  Amber, 0f, 550_000_000f, 0.2f, 5.0f),
new("disk.all.write_bytes", ShapeType.Cylinder, "Write", Amber, 0f, 550_000_000f, 0.2f, 5.0f),
```

And update `PerDiskZone()` (lines 81-82):

```csharp
new("disk.dev.read_bytes",  ShapeType.Cylinder, "Read",  DarkGreen, 0f, 550_000_000f, 0.2f, 5.0f),
new("disk.dev.write_bytes", ShapeType.Cylinder, "Write", DarkGreen, 0f, 550_000_000f, 0.2f, 5.0f),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/pmview-projection-core/tests/PmviewProjectionCore.Tests/ --filter "SourceRangeMax_MatchesPreset"`
Expected: PASS

- [ ] **Step 5: Run full test suite — check for regressions**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All PASS. Some TscnWriter snapshot tests may need updating if they assert exact SourceRangeMax values in generated output.

- [ ] **Step 6: Commit**

```bash
git add src/pmview-projection-core/
git commit -m "Align SharedZones defaults to hardware presets (SATA SSD, 1 Gbit)"
```

---

### Task 2: Add GetZoneCentroid to SceneBinder

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs`
- Test: `src/pmview-bridge-addon/test/SceneBinderTests.cs`

- [ ] **Step 1: Write compilation-check test**

SceneBinder requires the Godot runtime (GdUnit4), so we can only verify the API exists. Add to `SceneBinderTests.cs`:

```csharp
[TestCase]
[RequireGodotRuntime]
public void GetZoneCentroid_CanBeCalled()
{
    var binder = new SceneBinder();
    var centroid = binder.GetZoneCentroid("Disk");
    // Empty binder returns Vector3.Zero
    AssertThat(centroid).IsEqual(Godot.Vector3.Zero);
}
```

Note: Check existing test patterns in the file — use `AssertThat()` from GdUnit4, include `[RequireGodotRuntime]`.

- [ ] **Step 2: Implement GetZoneCentroid**

Add to `SceneBinder.cs` as a public method:

```csharp
/// <summary>
/// Returns the spatial centroid of all Node3D targets in the named zone.
/// Used by the range tuning panel for camera auto-focus.
/// </summary>
public Vector3 GetZoneCentroid(string zoneName)
{
    var seen = new HashSet<Node3D>();
    foreach (var active in _activeBindings)
    {
        if (active.Resolved.Binding.ZoneName != zoneName) continue;
        if (active.TargetNode is Node3D node3D)
            seen.Add(node3D);  // Dedup by node identity — multiple bindings may target same node
    }

    if (seen.Count == 0)
        return Vector3.Zero;

    var sum = Vector3.Zero;
    foreach (var node in seen)
        sum += node.GlobalPosition;
    return sum / seen.Count;
}
```

- [ ] **Step 3: Build addon to verify compilation**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-bridge-addon/
git commit -m "Add GetZoneCentroid to SceneBinder for camera auto-focus"
```

---

### Task 3: Add focus_on_position to Camera

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd`

No automated test — camera behaviour is verified visually in Godot.

- [ ] **Step 1: Add focus state variables**

Add after the existing transition state variables (after line 29):

```gdscript
# Focus state (smooth look-at for auto-focus)
var _focus_target: Vector3 = Vector3.ZERO
var _focus_active: bool = false
var _focus_start_yaw: float = 0.0
var _focus_start_pitch: float = 0.0
var _focus_target_yaw: float = 0.0
var _focus_target_pitch: float = 0.0
var _focus_progress: float = 0.0
const FOCUS_DURATION: float = 0.5
```

- [ ] **Step 2: Add focus_on_position method**

Add after `_ease_in_out()`:

```gdscript
## Smoothly pans the camera to look at the given world position.
## In orbit mode: changes orbit_center so the camera orbits the target.
## In fly mode: smoothly rotates to look at the target without moving.
func focus_on_position(target: Vector3) -> void:
	_focus_target = target
	match _mode:
		Mode.ORBIT:
			orbit_center = target
			# Orbit will naturally look_at the new centre on next frame
		Mode.FLY:
			# Compute target yaw/pitch from current position to target
			var dir := (target - global_position).normalized()
			_focus_target_yaw = atan2(-dir.x, -dir.z)
			_focus_target_pitch = asin(dir.y)
			_focus_start_yaw = _fly_yaw
			_focus_start_pitch = _fly_pitch
			_focus_progress = 0.0
			_focus_active = true
		Mode.TRANSITIONING:
			# Snap transition to orbit, then refocus
			_mode = Mode.ORBIT
			orbit_center = target
```

- [ ] **Step 3: Add focus interpolation to _process_fly**

In `_process_fly()`, add at the top (before the speed calculation):

```gdscript
	# Handle focus animation
	if _focus_active:
		_focus_progress += delta / FOCUS_DURATION
		var t := _ease_in_out(_focus_progress)
		_fly_yaw = lerpf(_focus_start_yaw, _focus_target_yaw, t)
		_fly_pitch = lerpf(_focus_start_pitch, _focus_target_pitch, t)
		if _focus_progress >= 1.0:
			_focus_active = false
```

- [ ] **Step 4: Cancel focus on manual input**

In `_unhandled_input()`, add after the TAB handling (before the fly-mode mouse block):

```gdscript
	# Cancel focus animation on any manual camera input
	if _focus_active:
		if event is InputEventMouseMotion and _is_right_clicking:
			_focus_active = false
```

Also in `_process_fly()`, cancel focus if WASD/QE is pressed. Add after the focus animation block:

```gdscript
		# Cancel focus if user takes manual control
		if input_dir.length_squared() > 0.0:
			_focus_active = false
```

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd
git commit -m "Add focus_on_position for smooth camera panning to zone targets"
```

---

## Chunk 2: HUD Bar

A standalone component — no dependencies on the tuning panel.

### Task 4: Create HUD Bar Scene and Script

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.tscn`
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.gd`

No automated test — visual UI component.

- [ ] **Step 1: Create the HUD bar script**

Create `src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.gd`:

```gdscript
extends PanelContainer

## Persistent keybinding HUD bar at the bottom of the viewport.
## Shows available controls. Highlights F1 when the tuner is active.

@onready var f1_label: Label = %F1Label

var _tuner_active: bool = false


func set_tuner_active(active: bool) -> void:
	_tuner_active = active
	if f1_label:
		f1_label.add_theme_color_override(
			"font_color", Color(1.0, 0.95, 0.8) if active else Color(0.97, 0.57, 0.09))
```

- [ ] **Step 2: Create the HUD bar scene**

Create `src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.tscn`:

```
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://addons/pmview-bridge/ui/hud_bar.gd" id="1"]

[sub_resource type="StyleBoxFlat" id="StyleBoxFlat_1"]
bg_color = Color(0.0, 0.0, 0.0, 0.7)
content_margin_left = 16.0
content_margin_top = 4.0
content_margin_right = 16.0
content_margin_bottom = 4.0

[node name="HudBar" type="PanelContainer"]
anchors_preset = 12
anchor_top = 1.0
anchor_right = 1.0
anchor_bottom = 1.0
offset_top = -28.0
grow_horizontal = 2
grow_vertical = 0
mouse_filter = 2
theme_override_styles/panel = SubResource("StyleBoxFlat_1")
script = ExtResource("1")

[node name="Keys" type="HBoxContainer" parent="."]
layout_mode = 2
alignment = 1
theme_override_constants/separation = 24

[node name="TabKey" type="Label" parent="Keys"]
layout_mode = 2
text = "TAB Mode"
theme_override_colors/font_color = Color(0.97, 0.57, 0.09, 1.0)
theme_override_font_sizes/font_size = 12

[node name="WasdKey" type="Label" parent="Keys"]
layout_mode = 2
text = "WASD Move"
theme_override_colors/font_color = Color(0.97, 0.57, 0.09, 1.0)
theme_override_font_sizes/font_size = 12

[node name="QeKey" type="Label" parent="Keys"]
layout_mode = 2
text = "Q/E Elevation"
theme_override_colors/font_color = Color(0.97, 0.57, 0.09, 1.0)
theme_override_font_sizes/font_size = 12

[node name="F1Label" type="Label" parent="Keys"]
unique_name_in_owner = true
layout_mode = 2
text = "F1 Tuner"
theme_override_colors/font_color = Color(0.97, 0.57, 0.09, 1.0)
theme_override_font_sizes/font_size = 12

[node name="EscKey" type="Label" parent="Keys"]
layout_mode = 2
text = "ESC Menu"
theme_override_colors/font_color = Color(0.97, 0.57, 0.09, 1.0)
theme_override_font_sizes/font_size = 12
```

Key details:
- `anchors_preset = 12` (PRESET_BOTTOM_WIDE) — spans full width at bottom
- `mouse_filter = 2` (MOUSE_FILTER_IGNORE) — does not intercept mouse events
- Labels use amber colour `Color(0.97, 0.57, 0.09)` matching the project's Orange palette
- Font size 12 — small but readable

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.gd src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.tscn
git commit -m "Add nano-style HUD bar showing keybindings at bottom of viewport"
```

---

### Task 5: Include HUD Bar in Generated and Runtime Scenes

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Modify: `src/pmview-app/scripts/RuntimeSceneBuilder.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write failing test for HUD bar in generated scenes**

Add to `TscnWriterTests.cs`:

```csharp
[Fact]
public void Write_ContainsHudBarInstance()
{
    var layout = CreateMinimalLayout();
    var result = TscnWriter.Write(layout);

    Assert.Contains("hud_bar", result);
    Assert.Contains("[node name=\"HudBar\"", result);
}
```

Use the same `CreateMinimalLayout()` helper used by existing tests (or the simplest available layout helper).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests/ --filter "HudBar"`
Expected: FAIL — TscnWriter doesn't emit HudBar.

- [ ] **Step 3: Register hud_bar ext_resource in TscnWriter**

In `TscnWriter.cs`, find where `range_tuning_panel_scene` is registered as an ext_resource. Add a similar registration for `hud_bar_scene`:

```csharp
registry.Register("hud_bar_scene", "PackedScene", "res://addons/pmview-bridge/ui/hud_bar.tscn");
```

- [ ] **Step 4: Add HudBar node emission to the existing WriteRangeTuningPanel method**

The existing `WriteRangeTuningPanel` method writes the UILayer CanvasLayer. Add HudBar instance to the same UILayer (the CanvasLayer serves as the shared UI layer for both HUD bar and tuning panel — no dedicated CanvasLayer per component needed):

```csharp
sb.AppendLine("[node name=\"HudBar\" parent=\"UILayer\" instance=ExtResource(\"hud_bar_scene\")]");
sb.AppendLine();
```

- [ ] **Step 5: Add HudBar to RuntimeSceneBuilder**

In `RuntimeSceneBuilder.cs`, find `AddRangeTuningPanel()`. Add HudBar instantiation to the same UILayer canvas:

```csharp
var hudScene = GD.Load<PackedScene>("res://addons/pmview-bridge/ui/hud_bar.tscn");
if (hudScene != null)
{
    var hud = hudScene.Instantiate();
    hud.Name = "HudBar";
    canvas.AddChild(hud);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests/ --filter "HudBar"`
Expected: PASS

- [ ] **Step 7: Run full test suite**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All PASS. Update `load_steps` assertions in existing tests if needed (+1 for new ext_resource).

- [ ] **Step 8: Commit**

```bash
git add src/pmview-host-projector/ src/pmview-app/
git commit -m "Include HUD bar in generated and runtime-built scenes"
```

---

## Chunk 3: Range Tuning Modal v2

Replace the v1 slider panel with preset buttons. This is the main UI rewrite.

### Task 6: Rewrite Range Tuning Panel Script (Preset Buttons)

**Files:**
- Rewrite: `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.gd`

- [ ] **Step 1: Rewrite the panel script**

Replace `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.gd` entirely:

```gdscript
extends Control

## Range tuning modal — preset buttons for disk and network hardware speeds.
## Click a preset to instantly apply via SceneBinder.UpdateSourceRangeMax().
## F1 toggles open/close. ESC also closes.

# -- Zone definitions: each maps a display label to API zone name(s) --
# Disk and Per-Disk share the same presets.
const DISK_PRESETS: Dictionary = {
	"HDD": 150_000_000.0,
	"SATA SSD": 550_000_000.0,
	"NVMe Gen3": 3_500_000_000.0,
	"NVMe Gen4": 7_000_000_000.0,
	"NVMe Gen5": 14_000_000_000.0,
}

const NETWORK_PRESETS: Dictionary = {
	"1 Gbit": 125_000_000.0,
	"10 Gbit": 1_250_000_000.0,
	"25 Gbit": 3_125_000_000.0,
	"40 Gbit": 5_000_000_000.0,
	"100 Gbit": 12_500_000_000.0,
}

# Zone column config: [display_label, api_zone_names, presets, colour]
const ZONE_COLUMNS: Array = [
	["Disk Total", ["Disk"], "disk", Color(0.97, 0.57, 0.09)],
	["Per-Disk", ["Per-Disk"], "disk", Color(0.13, 0.77, 0.37)],
	["Network", ["Network In", "Network Out"], "network", Color(0.23, 0.51, 0.96)],
]

var _scene_binder: Node = null
var _camera: Node = null
var _columns: Dictionary = {}  # zone_api_name -> {container, buttons, active_value}

@onready var _overlay: ColorRect = %Overlay


func _ready() -> void:
	visible = false
	_overlay.color = Color(0, 0, 0, 0.35)
	_overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE


func initialise(scene_binder: Node) -> void:
	_scene_binder = scene_binder
	# Find the camera for auto-focus
	_camera = get_viewport().get_camera_3d()
	if scene_binder.IsBound:
		_build_ui()
	else:
		scene_binder.connect("BindingsReady", _build_ui)


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed:
		if event.physical_keycode == KEY_F1:
			_toggle_panel()
			get_viewport().set_input_as_handled()
		elif event.physical_keycode == KEY_ESCAPE and visible:
			_close_panel()
			get_viewport().set_input_as_handled()


func _toggle_panel() -> void:
	if visible:
		_close_panel()
	else:
		_open_panel()


func _open_panel() -> void:
	visible = true
	if _overlay:
		_overlay.mouse_filter = Control.MOUSE_FILTER_STOP
	# Notify HUD bar
	var hud = get_parent().find_child("HudBar")
	if hud and hud.has_method("set_tuner_active"):
		hud.set_tuner_active(true)


func _close_panel() -> void:
	visible = false
	if _overlay:
		_overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var hud = get_parent().find_child("HudBar")
	if hud and hud.has_method("set_tuner_active"):
		hud.set_tuner_active(false)


func _build_ui() -> void:
	var ranges: Dictionary = _scene_binder.GetSourceRanges()

	# Build the column layout programmatically
	for col_def in ZONE_COLUMNS:
		var display_name: String = col_def[0]
		var api_zones: Array = col_def[1]
		var preset_type: String = col_def[2]
		var colour: Color = col_def[3]

		# Check if any of this column's zones are present
		var active_zone: String = ""
		var current_value: float = 0.0
		for zone_name in api_zones:
			if ranges.has(zone_name):
				active_zone = zone_name
				current_value = ranges[zone_name]
				break

		if active_zone.is_empty():
			continue  # Zone not present — skip column

		var presets: Dictionary = DISK_PRESETS if preset_type == "disk" else NETWORK_PRESETS
		_add_zone_column(display_name, api_zones, presets, colour, current_value)


func _add_zone_column(display_name: String, api_zones: Array,
		presets: Dictionary, colour: Color, current_value: float) -> void:
	var column: VBoxContainer = %ColumnsContainer.get_node_or_null(display_name.replace(" ", ""))
	if column == null:
		column = VBoxContainer.new()
		column.name = display_name.replace(" ", "")
		%ColumnsContainer.add_child(column)

	# Zone header
	var header := Label.new()
	header.text = display_name
	header.add_theme_color_override("font_color", colour)
	header.add_theme_font_size_override("font_size", 13)
	header.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(header)

	# Preset buttons
	var buttons: Array[Button] = []
	for preset_name: String in presets:
		var bytes_val: float = presets[preset_name]
		var btn := Button.new()
		btn.text = "%s  %s" % [preset_name, _format_bytes(bytes_val)]
		btn.custom_minimum_size = Vector2(160, 0)
		btn.alignment = HORIZONTAL_ALIGNMENT_LEFT

		# Style: muted by default
		btn.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))

		# Highlight if this preset matches current value
		if absf(bytes_val - current_value) < 1.0:
			_highlight_button(btn, colour)

		btn.pressed.connect(_on_preset_pressed.bind(api_zones, bytes_val, colour, buttons, btn))
		column.add_child(btn)
		buttons.append(btn)

	_columns[display_name] = {"buttons": buttons, "colour": colour}


func _on_preset_pressed(api_zones: Array, bytes_val: float,
		colour: Color, all_buttons: Array[Button], pressed_btn: Button) -> void:
	# Apply to all API zones in this column
	for zone_name: String in api_zones:
		_scene_binder.UpdateSourceRangeMax(zone_name, bytes_val)

	# Update highlights — deselect all, highlight pressed
	for btn: Button in all_buttons:
		_unhighlight_button(btn)
	_highlight_button(pressed_btn, colour)

	# Auto-focus camera on the first zone
	if _camera and _camera.has_method("focus_on_position"):
		var centroid: Vector3 = _scene_binder.GetZoneCentroid(api_zones[0])
		if centroid != Vector3.ZERO:
			_camera.focus_on_position(centroid)


func _highlight_button(btn: Button, colour: Color) -> void:
	btn.add_theme_color_override("font_color", colour)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(colour, 0.15)
	style.border_color = colour
	style.set_border_width_all(1)
	style.set_corner_radius_all(4)
	style.set_content_margin_all(4)
	btn.add_theme_stylebox_override("normal", style)


func _unhighlight_button(btn: Button) -> void:
	btn.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
	btn.remove_theme_stylebox_override("normal")


static func _format_bytes(bytes_per_sec: float) -> String:
	if bytes_per_sec >= 1_000_000_000.0:
		return "%.1f GB/s" % (bytes_per_sec / 1_000_000_000.0)
	elif bytes_per_sec >= 1_000_000.0:
		return "%.0f MB/s" % (bytes_per_sec / 1_000_000.0)
	elif bytes_per_sec >= 1_000.0:
		return "%.0f KB/s" % (bytes_per_sec / 1_000.0)
	else:
		return "%.0f B/s" % bytes_per_sec
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.gd
git commit -m "Rewrite range tuning panel with preset buttons replacing sliders"
```

---

### Task 7: Rewrite Range Tuning Panel Scene (Horizontal Layout)

**Files:**
- Rewrite: `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.tscn`

- [ ] **Step 1: Rewrite the panel scene**

Replace `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.tscn` entirely:

```
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://addons/pmview-bridge/ui/range_tuning_panel.gd" id="1"]

[sub_resource type="StyleBoxFlat" id="StyleBoxFlat_1"]
bg_color = Color(0.086, 0.086, 0.157, 0.95)
corner_radius_top_left = 14
corner_radius_top_right = 14
corner_radius_bottom_right = 14
corner_radius_bottom_left = 14
border_color = Color(0.27, 0.27, 0.27, 1.0)
border_width_left = 1
border_width_top = 1
border_width_right = 1
border_width_bottom = 1
content_margin_left = 22.0
content_margin_top = 18.0
content_margin_right = 22.0
content_margin_bottom = 18.0
shadow_color = Color(0.0, 0.0, 0.0, 0.5)
shadow_size = 8

[node name="RangeTuningPanel" type="Control"]
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
mouse_filter = 2
script = ExtResource("1")

[node name="Overlay" type="ColorRect" parent="."]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
color = Color(0, 0, 0, 0.35)
mouse_filter = 2

[node name="ModalPanel" type="PanelContainer" parent="."]
layout_mode = 1
anchors_preset = 8
anchor_left = 0.5
anchor_top = 0.5
anchor_right = 0.5
anchor_bottom = 0.5
offset_left = -360.0
offset_top = -150.0
offset_right = 360.0
offset_bottom = 150.0
grow_horizontal = 2
grow_vertical = 2
theme_override_styles/panel = SubResource("StyleBoxFlat_1")

[node name="Layout" type="VBoxContainer" parent="ModalPanel"]
layout_mode = 2

[node name="Header" type="HBoxContainer" parent="ModalPanel/Layout"]
layout_mode = 2

[node name="Title" type="Label" parent="ModalPanel/Layout/Header"]
layout_mode = 2
size_flags_horizontal = 3
text = "Range Tuning"
theme_override_colors/font_color = Color(0.87, 0.87, 0.87, 1.0)
theme_override_font_sizes/font_size = 15

[node name="CloseHint" type="Label" parent="ModalPanel/Layout/Header"]
layout_mode = 2
text = "F1 or ESC to close"
theme_override_colors/font_color = Color(0.4, 0.4, 0.4, 1.0)
theme_override_font_sizes/font_size = 11

[node name="Separator" type="HSeparator" parent="ModalPanel/Layout"]
layout_mode = 2

[node name="ColumnsContainer" type="HBoxContainer" parent="ModalPanel/Layout"]
unique_name_in_owner = true
layout_mode = 2
theme_override_constants/separation = 16
```

Key details:
- Root node is a full-rect `Control` (invisible container) — holds both overlay and modal panel
- `Overlay` is a full-screen `ColorRect` with `rgba(0,0,0,0.35)` — blocks mouse events when panel is open
- `ModalPanel` is centred on screen (anchors_preset = 8, PRESET_CENTER), 720px wide × 300px tall
- Dark background with subtle border and shadow on the ModalPanel
- `ColumnsContainer` is the target for programmatically-added zone columns (from the script's `_build_ui`)
- Only the container structure is in the .tscn — columns and buttons are built dynamically based on which zones exist
- The root Control has `mouse_filter = 2` (MOUSE_FILTER_IGNORE) — only the Overlay blocks mouse events when activated

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.tscn
git commit -m "Rewrite panel scene with centred modal layout for preset buttons"
```

---

### Task 8: Verify and Update Host View Controller Wiring

**Files:**
- Verify: `src/pmview-bridge-addon/addons/pmview-bridge/host_view_controller.gd`
- Verify: `src/pmview-app/scripts/HostViewController.gd`

The v2 panel has a different scene structure (root is `Control` instead of `PanelContainer`, panel starts hidden). We need to verify existing wiring still works.

- [ ] **Step 1: Read and verify host_view_controller.gd wiring**

Read `src/pmview-bridge-addon/addons/pmview-bridge/host_view_controller.gd`. The existing code (lines 31-35) does:
```gdscript
var tuning_panel = find_child("RangeTuningPanel")
if tuning_panel:
    tuning_panel.initialise(binder)
```

Verify:
1. `find_child("RangeTuningPanel")` still works — the node name in the .tscn is still `RangeTuningPanel` (root Control node). ✓
2. `initialise(binder)` is called after `BindFromSceneProperties()` and `StartPolling()` — which means `IsBound` is already true when `initialise()` runs, so `_build_ui()` is called synchronously. ✓
3. `get_viewport().get_camera_3d()` in `initialise()` — at this point the camera node is in the scene tree (added during scene build), so this returns the camera. ✓

No changes needed to the controller — the existing wiring is correct for v2.

- [ ] **Step 2: Read and verify HostViewController.gd ESC ordering**

Read `src/pmview-app/scripts/HostViewController.gd`. The ESC handling is in `_unhandled_input()`. The tuning panel (child node) processes `_unhandled_input()` before the parent `HostViewController` in Godot's propagation order. So when the tuner is open and ESC is pressed:
1. Tuning panel sees ESC → closes panel → calls `set_input_as_handled()` → event consumed
2. HostViewController never sees the event

When tuner is closed:
1. Tuning panel sees ESC → `visible` is false → ignores it
2. HostViewController sees ESC → double-tap menu logic

Verify this ordering is correct. No code changes needed.

- [ ] **Step 3: Build addon to verify no breakage**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds.

---

### Task 9: Full Solution Build + Test Verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build pmview-nextgen.sln`
Expected: Clean build, zero errors.

- [ ] **Step 2: Run all non-integration tests**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests PASS.

- [ ] **Step 3: Final commit if any loose ends**

Only if needed — all prior tasks should have committed their changes.

---

## Dependency Summary

```
Task 1 (SharedZones defaults) ── independent
Task 2 (GetZoneCentroid) ── independent
Task 3 (Camera focus_on_position) ── independent
Task 4 (HUD bar scene/script) ── independent
Task 5 (HUD bar in generated scenes) ── depends on Task 4
Task 6 (Panel script rewrite) ── depends on Tasks 2, 3
Task 7 (Panel scene rewrite) ── independent (scene structure only)
Task 8 (Wiring verification) ── depends on Tasks 4, 6, 7
Task 9 (Full verification) ── depends on all above
```

Tasks 1, 2, 3, 4, 7 can all run in parallel.
