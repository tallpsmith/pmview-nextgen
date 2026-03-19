# Floating Help Panel Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the always-visible HudBar with an on-demand floating Help panel (H/?) and cycling hint system.

**Architecture:** Addon provides reusable `HelpPanel` and `HelpHint` GDScript nodes in `addons/pmview-bridge/ui/`. App populates content and wires input in `HostViewController`. HudBar removed from all 6 touch-points. Camera gets an `input_enabled` flag for proper suppression.

**Tech Stack:** GDScript (Godot 4.6), C# (.NET 8.0) for RuntimeSceneBuilder/TscnWriter changes, xUnit for C# tests.

**Spec:** `docs/superpowers/specs/2026-03-19-floating-help-panel-design.md`
**GitHub Issue:** #50

---

## Chunk 1: Addon — Data Classes and HelpPanel Node

### Task 1: Add `input_enabled` flag to FlyOrbitCamera

This is a prerequisite for proper camera suppression when any panel is open.

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd:62-139`

- [ ] **Step 1: Add `input_enabled` property**

At the top of `fly_orbit_camera.gd`, after line 14 (`@export var transition_speed`), add:

```gdscript
## When false, all movement and mouse-look input is ignored.
## Used by panels (Help, Range Tuning) to suppress camera while open.
var input_enabled: bool = true
```

- [ ] **Step 2: Guard `_unhandled_input`**

Wrap the body of `_unhandled_input()` (line 46) with an early return:

```gdscript
func _unhandled_input(event: InputEvent) -> void:
	if not input_enabled:
		return
	# ... existing body unchanged ...
```

- [ ] **Step 3: Guard `_process_fly`**

At the top of `_process_fly()` (line 101), add an early return:

```gdscript
func _process_fly(delta: float) -> void:
	if not input_enabled:
		return
	# ... existing body unchanged ...
```

- [ ] **Step 4: Test manually**

This is a GDScript-only change — no automated test possible from CLI. Verify the property exists by checking the file compiles (it will be tested end-to-end when we wire up the HelpPanel). Mark as done.

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd
git commit -m "Add input_enabled flag to FlyOrbitCamera

Panels can now suppress camera movement by setting input_enabled=false.
Fixes pre-existing WASD leak through tuner overlay. Ref #50"
```

---

### Task 2: Create HelpGroup and HelpHintEntry data classes

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/help_group.gd`
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint_entry.gd`

- [ ] **Step 1: Create `help_group.gd`**

```gdscript
class_name HelpGroup
extends RefCounted

## A named group of key bindings for the HelpPanel.
## Each group has a header colour and can be individually enabled/disabled.

var group_name: String
var header_color: Color
var entries: Array[HelpEntry] = []
var enabled: bool = true


static func create(p_name: String, p_color: Color, p_entries: Array[HelpEntry],
		p_enabled: bool = true) -> HelpGroup:
	var g := HelpGroup.new()
	g.group_name = p_name
	g.header_color = p_color
	g.entries = p_entries
	g.enabled = p_enabled
	return g


class HelpEntry extends RefCounted:
	var key_text: String
	var action_text: String
	var enabled: bool = true

	static func create(p_key: String, p_action: String,
			p_enabled: bool = true) -> HelpEntry:
		var e := HelpEntry.new()
		e.key_text = p_key
		e.action_text = p_action
		e.enabled = p_enabled
		return e
```

- [ ] **Step 2: Create `help_hint_entry.gd`**

```gdscript
class_name HelpHintEntry
extends RefCounted

## A single cycling hint entry for the HelpHint node.

var key_text: String
var action_text: String


static func create(p_key: String, p_action: String) -> HelpHintEntry:
	var h := HelpHintEntry.new()
	h.key_text = p_key
	h.action_text = p_action
	return h
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/help_group.gd \
        src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint_entry.gd
git commit -m "Add HelpGroup and HelpHintEntry data classes

Typed data model for the Help panel content API. Ref #50"
```

---

### Task 3: Create HelpPanel scene and script

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/help_panel.tscn`
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/help_panel.gd`

- [ ] **Step 1: Create `help_panel.gd`**

```gdscript
extends PanelContainer

## Floating translucent help panel listing controls in grouped sections.
## Toggle with show_panel() / hide_panel() / toggle().
## Content set via set_groups() — the addon provides the mechanism,
## the consuming app provides the content.

signal panel_opened
signal panel_closed

const HEADER_FONT_SIZE := 11
const KEY_FONT_SIZE := 13
const ACTION_FONT_SIZE := 13
const KEY_COLUMN_WIDTH := 110
const DISABLED_OPACITY := 0.3

var _groups: Array[HelpGroup] = []
var _group_containers: Dictionary = {}  # group_name -> VBoxContainer


func _ready() -> void:
	visible = false
	mouse_filter = Control.MOUSE_FILTER_STOP


func set_groups(groups: Array[HelpGroup]) -> void:
	_groups = groups
	_rebuild_ui()


func set_group_enabled(group_name: String, enabled: bool) -> void:
	for group in _groups:
		if group.group_name == group_name:
			group.enabled = enabled
	if _group_containers.has(group_name):
		var container: VBoxContainer = _group_containers[group_name]
		container.modulate.a = 1.0 if enabled else DISABLED_OPACITY


func set_entry_enabled(group_name: String, key_text: String, enabled: bool) -> void:
	for group in _groups:
		if group.group_name == group_name:
			for entry in group.entries:
				if entry.key_text == key_text:
					entry.enabled = enabled
	# Rebuild is simplest for per-entry changes
	_rebuild_ui()


func toggle() -> void:
	if visible:
		hide_panel()
	else:
		show_panel()


func show_panel() -> void:
	if visible:
		return
	visible = true
	panel_opened.emit()


func hide_panel() -> void:
	if not visible:
		return
	visible = false
	panel_closed.emit()


func _rebuild_ui() -> void:
	var content: VBoxContainer = %Content
	# Clear existing children (immediate free — these are our own UI nodes)
	for child in content.get_children():
		content.remove_child(child)
		child.free()
	_group_containers.clear()

	for group in _groups:
		var group_box := VBoxContainer.new()
		group_box.add_theme_constant_override("separation", 4)
		if not group.enabled:
			group_box.modulate.a = DISABLED_OPACITY
		_group_containers[group.group_name] = group_box

		# Group header
		var header := Label.new()
		header.text = group.group_name.to_upper()
		if not group.enabled:
			header.text += "  (archive mode only)"
		header.add_theme_font_size_override("font_size", HEADER_FONT_SIZE)
		header.add_theme_color_override("font_color", group.header_color)
		header.uppercase = true
		group_box.add_child(header)

		# Entries grid
		var grid := GridContainer.new()
		grid.columns = 2
		grid.add_theme_constant_override("h_separation", 12)
		grid.add_theme_constant_override("v_separation", 4)

		for entry in group.entries:
			var key_label := Label.new()
			key_label.text = entry.key_text
			key_label.custom_minimum_size.x = KEY_COLUMN_WIDTH
			key_label.add_theme_font_size_override("font_size", KEY_FONT_SIZE)
			key_label.add_theme_color_override("font_color", Color(0.878, 0.816, 1.0))
			if not entry.enabled:
				key_label.modulate.a = DISABLED_OPACITY

			var action_label := Label.new()
			action_label.text = entry.action_text
			action_label.add_theme_font_size_override("font_size", ACTION_FONT_SIZE)
			action_label.add_theme_color_override("font_color", Color(1.0, 1.0, 1.0, 0.7))
			if not entry.enabled:
				action_label.modulate.a = DISABLED_OPACITY
				var suffix := ""
				# Check if this is a per-entry disable (not whole group)
				if group.enabled:
					suffix = "  (archive)"
					action_label.text += suffix

			grid.add_child(key_label)
			grid.add_child(action_label)

		group_box.add_child(grid)
		content.add_child(group_box)

		# Spacer between groups (except last)
		if group != _groups.back():
			var spacer := Control.new()
			spacer.custom_minimum_size.y = 12
			content.add_child(spacer)
```

- [ ] **Step 2: Create `help_panel.tscn`**

```tscn
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://addons/pmview-bridge/ui/help_panel.gd" id="1"]

[sub_resource type="StyleBoxFlat" id="StyleBoxFlat_panel"]
bg_color = Color(0.039, 0.039, 0.118, 0.88)
border_color = Color(0.51, 0.314, 0.863, 0.4)
border_width_left = 1
border_width_top = 1
border_width_right = 1
border_width_bottom = 1
corner_radius_top_left = 12
corner_radius_top_right = 12
corner_radius_bottom_left = 12
corner_radius_bottom_right = 12
content_margin_left = 36.0
content_margin_top = 28.0
content_margin_right = 36.0
content_margin_bottom = 28.0

[node name="HelpPanel" type="PanelContainer"]
anchors_preset = 8
anchor_left = 0.5
anchor_top = 0.5
anchor_right = 0.5
anchor_bottom = 0.5
offset_left = -260.0
offset_top = -200.0
offset_right = 260.0
offset_bottom = 200.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 1
theme_override_styles/panel = SubResource("StyleBoxFlat_panel")
script = ExtResource("1")

[node name="VBox" type="VBoxContainer" parent="."]
layout_mode = 2

[node name="Header" type="VBoxContainer" parent="VBox"]
layout_mode = 2
alignment = 1

[node name="Title" type="Label" parent="VBox/Header"]
layout_mode = 2
text = "CONTROLS"
horizontal_alignment = 1
theme_override_font_sizes/font_size = 18
theme_override_colors/font_color = Color(0.878, 0.816, 1.0, 1.0)

[node name="Subtitle" type="Label" parent="VBox/Header"]
layout_mode = 2
text = "H / ? / ESC to close"
horizontal_alignment = 1
theme_override_font_sizes/font_size = 11
theme_override_colors/font_color = Color(1.0, 1.0, 1.0, 0.35)

[node name="HeaderSpacer" type="Control" parent="VBox"]
layout_mode = 2
custom_minimum_size = Vector2(0, 16)

[node name="Content" type="VBoxContainer" parent="VBox"]
unique_name_in_owner = true
layout_mode = 2
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/help_panel.gd \
        src/pmview-bridge-addon/addons/pmview-bridge/ui/help_panel.tscn
git commit -m "Add HelpPanel floating overlay node

Centre-screen translucent panel with grouped key binding display.
Supports per-group and per-entry enable/disable for live vs archive. Ref #50"
```

---

### Task 4: Create HelpHint cycling node

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint.tscn`
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint.gd`

- [ ] **Step 1: Create `help_hint.gd`**

```gdscript
extends PanelContainer

## Subtle cycling hint in the bottom-right corner.
## Rotates through tips (15s visible, then 60s silent).
## Hidden when the HelpPanel is open or TimeControl is revealed.

signal visibility_changed(is_visible: bool)

const DISPLAY_DURATION := 15.0
const SILENT_DURATION := 60.0
const FADE_DURATION := 0.5

var _hints: Array[HelpHintEntry] = []
var _current_index: int = 0
var _is_cycling: bool = false
var _suppressed: bool = false  # external suppression (panel open, time control overlap)

@onready var _key_label: Label = %KeyLabel
@onready var _action_label: Label = %ActionLabel
@onready var _display_timer: Timer = %DisplayTimer
@onready var _silent_timer: Timer = %SilentTimer
@onready var _fade_tween: Tween = null


func _ready() -> void:
	modulate.a = 0.0
	visible = true  # always in tree, use alpha for visibility
	_display_timer.timeout.connect(_on_display_timeout)
	_silent_timer.timeout.connect(_on_silent_timeout)


func set_hints(hints: Array[HelpHintEntry]) -> void:
	_hints = hints
	_current_index = 0


func start_cycling() -> void:
	if _hints.is_empty():
		return
	_is_cycling = true
	_current_index = 0
	_show_current_hint()


func stop_cycling() -> void:
	_is_cycling = false
	_display_timer.stop()
	_silent_timer.stop()
	_fade_out()


func hide_hint() -> void:
	_suppressed = true
	_fade_out()
	visibility_changed.emit(false)


func resume_hint() -> void:
	_suppressed = false
	# Don't immediately show — let the current cycle state drive it.
	# If we were in the middle of displaying, the timer will handle it.


func _show_current_hint() -> void:
	if _hints.is_empty() or _suppressed:
		return
	var hint := _hints[_current_index]
	_key_label.text = hint.key_text
	_action_label.text = hint.action_text
	_fade_in()
	_display_timer.start(DISPLAY_DURATION)
	visibility_changed.emit(true)


func _on_display_timeout() -> void:
	_fade_out()
	_current_index += 1
	if _current_index < _hints.size():
		# Show next hint after fade completes
		get_tree().create_timer(FADE_DURATION).timeout.connect(_show_current_hint)
	else:
		# All hints shown — enter silent period
		_current_index = 0
		_silent_timer.start(SILENT_DURATION)
	visibility_changed.emit(false)


func _on_silent_timeout() -> void:
	if _is_cycling and not _suppressed:
		_show_current_hint()


func _fade_in() -> void:
	if _fade_tween:
		_fade_tween.kill()
	_fade_tween = create_tween()
	_fade_tween.tween_property(self, "modulate:a", 1.0, FADE_DURATION)


func _fade_out() -> void:
	if _fade_tween:
		_fade_tween.kill()
	_fade_tween = create_tween()
	_fade_tween.tween_property(self, "modulate:a", 0.0, FADE_DURATION)
```

- [ ] **Step 2: Create `help_hint.tscn`**

```tscn
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://addons/pmview-bridge/ui/help_hint.gd" id="1"]

[sub_resource type="StyleBoxFlat" id="StyleBoxFlat_pill"]
bg_color = Color(1.0, 1.0, 1.0, 0.05)
corner_radius_top_left = 4
corner_radius_top_right = 4
corner_radius_bottom_left = 4
corner_radius_bottom_right = 4
content_margin_left = 8.0
content_margin_top = 4.0
content_margin_right = 8.0
content_margin_bottom = 4.0

[node name="HelpHint" type="PanelContainer"]
anchors_preset = 3
anchor_left = 1.0
anchor_top = 1.0
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = -200.0
offset_top = -40.0
offset_right = -16.0
offset_bottom = -16.0
grow_horizontal = 0
grow_vertical = 0
mouse_filter = 2
theme_override_styles/panel = SubResource("StyleBoxFlat_pill")
script = ExtResource("1")

[node name="HBox" type="HBoxContainer" parent="."]
layout_mode = 2
theme_override_constants/separation = 6

[node name="KeyLabel" type="Label" parent="HBox"]
unique_name_in_owner = true
layout_mode = 2
theme_override_font_sizes/font_size = 12
theme_override_colors/font_color = Color(0.94, 0.56, 0.13, 0.5)

[node name="ActionLabel" type="Label" parent="HBox"]
unique_name_in_owner = true
layout_mode = 2
theme_override_font_sizes/font_size = 12
theme_override_colors/font_color = Color(1.0, 1.0, 1.0, 0.3)

[node name="DisplayTimer" type="Timer" parent="."]
unique_name_in_owner = true
one_shot = true

[node name="SilentTimer" type="Timer" parent="."]
unique_name_in_owner = true
one_shot = true
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint.gd \
        src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint.tscn
git commit -m "Add HelpHint cycling tip node

Bottom-right pill that rotates through key tips on a 15s/60s cycle.
Supports suppression when HelpPanel or TimeControl is active. Ref #50"
```

---

## Chunk 2: Remove HudBar and Wire Help Panel in App

### Task 5: Remove HudBar from RangeTuningPanel

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.gd:72-88`

- [ ] **Step 1: Remove HudBar calls from `_open_panel()` and `_close_panel()`**

In `range_tuning_panel.gd`, remove lines 76-79 from `_open_panel()`:

```gdscript
	# Notify HUD bar
	var hud = get_parent().find_child("HudBar")
	if hud and hud.has_method("set_tuner_active"):
		hud.set_tuner_active(true)
```

And remove lines 86-88 from `_close_panel()`:

```gdscript
	var hud = get_parent().find_child("HudBar")
	if hud and hud.has_method("set_tuner_active"):
		hud.set_tuner_active(false)
```

The resulting methods should be:

```gdscript
func _open_panel() -> void:
	visible = true
	if _overlay:
		_overlay.mouse_filter = Control.MOUSE_FILTER_STOP


func _close_panel() -> void:
	visible = false
	if _overlay:
		_overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE
```

- [ ] **Step 2: Add `panel_opened` and `panel_closed` signals**

At the top of `range_tuning_panel.gd`, after the class doc comment (after line 5), add:

```gdscript
signal panel_opened
signal panel_closed
```

Then emit them in the open/close methods:

```gdscript
func _open_panel() -> void:
	visible = true
	if _overlay:
		_overlay.mouse_filter = Control.MOUSE_FILTER_STOP
	panel_opened.emit()


func _close_panel() -> void:
	visible = false
	if _overlay:
		_overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel_closed.emit()


## Public API for external callers (panel exclusivity).
func close_panel() -> void:
	if visible:
		_close_panel()
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.gd
git commit -m "Remove HudBar coupling from RangeTuningPanel

Drop find_child('HudBar') calls, add panel_opened/panel_closed signals
for the new camera suppression mechanism. Ref #50"
```

---

### Task 6: Delete HudBar files

**Files:**
- Delete: `src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.tscn`
- Delete: `src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.gd`

- [ ] **Step 1: Delete the files**

```bash
git rm src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.tscn \
       src/pmview-bridge-addon/addons/pmview-bridge/ui/hud_bar.gd
```

- [ ] **Step 2: Commit**

```bash
git commit -m "Delete HudBar — replaced by HelpPanel + HelpHint

Ref #50"
```

---

### Task 7: Update TscnWriter and tests (C#)

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs:52-55,238-248`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs:661-674`

- [ ] **Step 1: Update failing tests first (TDD)**

In `TscnWriterTests.cs`, replace the two HudBar tests (lines 661-674) with HelpPanel + HelpHint tests:

```csharp
[Fact]
public void Write_ContainsHelpPanelInstance()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("help_panel", tscn);
    Assert.Contains("[node name=\"HelpPanel\" parent=\"UILayer\" instance=ExtResource(\"help_panel_scene\")]", tscn);
}

[Fact]
public void Write_HasHelpPanelExtResource()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("res://addons/pmview-bridge/ui/help_panel.tscn", tscn);
    Assert.Contains("help_panel_scene", tscn);
}

[Fact]
public void Write_ContainsHelpHintInstance()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("help_hint", tscn);
    Assert.Contains("[node name=\"HelpHint\" parent=\"UILayer\" instance=ExtResource(\"help_hint_scene\")]", tscn);
}

[Fact]
public void Write_HasHelpHintExtResource()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("res://addons/pmview-bridge/ui/help_hint.tscn", tscn);
    Assert.Contains("help_hint_scene", tscn);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName~TscnWriterTests.Write_ContainsHelpPanel or FullyQualifiedName~TscnWriterTests.Write_HasHelpPanel or FullyQualifiedName~TscnWriterTests.Write_ContainsHelpHint or FullyQualifiedName~TscnWriterTests.Write_HasHelpHint" --no-build
```

Expected: FAIL — the implementation still emits HudBar, not HelpPanel/HelpHint.

- [ ] **Step 3: Update TscnWriter — ext_resource registration**

In `TscnWriter.cs`, replace lines 54-55:

```csharp
registry.Require("hud_bar_scene", "PackedScene",
    "res://addons/pmview-bridge/ui/hud_bar.tscn");
```

With:

```csharp
registry.Require("help_panel_scene", "PackedScene",
    "res://addons/pmview-bridge/ui/help_panel.tscn");
registry.Require("help_hint_scene", "PackedScene",
    "res://addons/pmview-bridge/ui/help_hint.tscn");
```

- [ ] **Step 4: Update TscnWriter — node emission**

In `WriteRangeTuningPanel()`, replace line 246:

```csharp
sb.AppendLine("[node name=\"HudBar\" parent=\"UILayer\" instance=ExtResource(\"hud_bar_scene\")]");
```

With:

```csharp
sb.AppendLine("[node name=\"HelpPanel\" parent=\"UILayer\" instance=ExtResource(\"help_panel_scene\")]");
sb.AppendLine();

sb.AppendLine("[node name=\"HelpHint\" parent=\"UILayer\" instance=ExtResource(\"help_hint_scene\")]");
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName~TscnWriterTests&FullyQualifiedName!~Integration"
```

Expected: ALL PASS. Old HudBar tests are gone, new HelpPanel/HelpHint tests pass.

- [ ] **Step 6: Run full test suite**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
```

Expected: ALL PASS.

- [ ] **Step 7: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Replace HudBar with HelpPanel+HelpHint in TscnWriter

Generated .tscn files now emit HelpPanel and HelpHint nodes instead
of the old HudBar. Tests updated to match. Ref #50"
```

---

### Task 8: Update RuntimeSceneBuilder (C#)

**Files:**
- Modify: `src/pmview-app/scripts/RuntimeSceneBuilder.cs:33,379-385`

- [ ] **Step 1: Replace HudBar scene path constant**

Replace line 33:

```csharp
private const string HudBarScenePath = "res://addons/pmview-bridge/ui/hud_bar.tscn";
```

With:

```csharp
private const string HelpPanelScenePath = "res://addons/pmview-bridge/ui/help_panel.tscn";
private const string HelpHintScenePath = "res://addons/pmview-bridge/ui/help_hint.tscn";
```

- [ ] **Step 2: Replace HudBar instantiation**

Replace lines 379-385:

```csharp
var hudScene = GD.Load<PackedScene>(HudBarScenePath);
if (hudScene != null)
{
    var hud = hudScene.Instantiate();
    hud.Name = "HudBar";
    canvas.AddChild(hud);
}
```

With:

```csharp
var helpPanelScene = GD.Load<PackedScene>(HelpPanelScenePath);
if (helpPanelScene != null)
{
    var helpPanel = helpPanelScene.Instantiate();
    helpPanel.Name = "HelpPanel";
    canvas.AddChild(helpPanel);
}

var helpHintScene = GD.Load<PackedScene>(HelpHintScenePath);
if (helpHintScene != null)
{
    var helpHint = helpHintScene.Instantiate();
    helpHint.Name = "HelpHint";
    canvas.AddChild(helpHint);
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build pmview-nextgen.ci.slnf
```

Expected: Build succeeds (RuntimeSceneBuilder uses Godot types, but ci.slnf should still compile).

Note: If RuntimeSceneBuilder is excluded from ci.slnf (it's in pmview-app which requires Godot SDK), this step may not compile. In that case, verify the edit is correct by inspection and move on — CI with Godot SDK will catch issues.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/RuntimeSceneBuilder.cs
git commit -m "Replace HudBar with HelpPanel+HelpHint in RuntimeSceneBuilder

Runtime scene building now instantiates the new help UI nodes. Ref #50"
```

---

### Task 9: Rewire HostViewController

This is the big integration task — remove all HudBar references, add H/? input handling, wire panel exclusivity and camera suppression.

**Files:**
- Modify: `src/pmview-app/scripts/HostViewController.gd`

- [ ] **Step 1: Remove HudBar variable and references**

Remove the `_hud_bar` variable declaration (line 12):

```gdscript
var _hud_bar: Node = null
```

Remove the duplicate `_hud_bar = scene.find_child(...)` calls and all HudBar label manipulation (lines 77, 85-95).

Remove the HudBar space label update block in `_on_playback_position_changed()` (lines 118-128):

```gdscript
	# Update HudBar play state
	if _hud_bar:
		var space_label = _hud_bar.find_child("SpaceLabel", false, false)
		if space_label:
			if mode == "Playback":
				space_label.text = "SPACE ⏸"
				space_label.add_theme_color_override("font_color",
					Color(0.298, 0.686, 0.314))
			else:
				space_label.text = "SPACE ▶"
				space_label.add_theme_color_override("font_color",
					Color(0.976, 0.451, 0.086))
```

- [ ] **Step 2: Add HelpPanel, HelpHint, and camera variables**

Add new variables at the top (replacing `_hud_bar`):

```gdscript
var _help_panel: Node = null
var _help_hint: Node = null
var _camera: Node = null
```

- [ ] **Step 3: Wire HelpPanel and HelpHint in `_ready()`**

After the scene is added to the tree (after `add_child(scene)`), add:

```gdscript
	# Wire Help panel and hint
	_help_panel = scene.find_child("HelpPanel", true, false)
	_help_hint = scene.find_child("HelpHint", true, false)
	_camera = get_viewport().get_camera_3d()

	# Connect panel signals for camera suppression
	if _help_panel:
		_help_panel.panel_opened.connect(_on_help_opened)
		_help_panel.panel_closed.connect(_on_help_closed)
		_setup_help_content()

	if _help_hint:
		_setup_help_hints()

	# Wire RangeTuningPanel camera suppression
	var tuning_panel := scene.find_child("RangeTuningPanel", true, false)
	if tuning_panel:
		if tuning_panel.has_signal("panel_opened"):
			tuning_panel.panel_opened.connect(_on_tuner_opened)
			tuning_panel.panel_closed.connect(_on_tuner_closed)
```

- [ ] **Step 4: Add help content setup methods**

```gdscript
func _setup_help_content() -> void:
	var orange := Color(0.94, 0.56, 0.13)
	var purple := Color(0.322, 0.137, 0.925)

	var camera_group := HelpGroup.create("Camera", orange, [
		HelpGroup.HelpEntry.create("TAB", "Toggle Orbit / Free Look"),
		HelpGroup.HelpEntry.create("W A S D", "Move (free look mode)"),
		HelpGroup.HelpEntry.create("Q / E", "Descend / Ascend"),
		HelpGroup.HelpEntry.create("SHIFT", "Sprint (hold with movement)"),
		HelpGroup.HelpEntry.create("Right-click", "Look around (hold + drag)"),
	])

	var archive_group := HelpGroup.create("Archive Mode Playback", purple, [
		HelpGroup.HelpEntry.create("SPACE", "Play / Pause"),
		HelpGroup.HelpEntry.create("← →", "Scrub (poll interval)"),
		HelpGroup.HelpEntry.create("⇧ ← →", "Scrub ±5 seconds"),
		HelpGroup.HelpEntry.create("⌃⇧ ← →", "Scrub ±1 minute"),
		HelpGroup.HelpEntry.create("R", "Reset time range"),
		HelpGroup.HelpEntry.create("Mouse → edge", "Show timeline panel"),
		HelpGroup.HelpEntry.create("Click", "Jump to time (on timeline)"),
		HelpGroup.HelpEntry.create("⇧ Click", "Set IN / OUT range markers"),
	], _is_archive_mode)

	var panels_group := HelpGroup.create("Panels", orange, [
		HelpGroup.HelpEntry.create("F1", "Range Tuning"),
		HelpGroup.HelpEntry.create("F2", "Timeline Navigator", _is_archive_mode),
		HelpGroup.HelpEntry.create("H / ?", "This help panel"),
	])

	var general_group := HelpGroup.create("General", orange, [
		HelpGroup.HelpEntry.create("ESC × 2", "Return to main menu"),
	])

	_help_panel.set_groups([camera_group, archive_group, panels_group, general_group])


func _setup_help_hints() -> void:
	_help_hint.set_hints([
		HelpHintEntry.create("H", "for Help"),
		HelpHintEntry.create("TAB", "Orbit / Free Look"),
	])
	_help_hint.start_cycling()
```

- [ ] **Step 5: Add panel signal handlers**

```gdscript
func _on_help_opened() -> void:
	if _camera:
		_camera.input_enabled = false
	if _help_hint:
		_help_hint.hide_hint()
	# Panel exclusivity — close tuner if open
	var scene_root := get_child(0) if get_child_count() > 0 else null
	if scene_root:
		var tuning_panel := scene_root.find_child("RangeTuningPanel", true, false)
		if tuning_panel and tuning_panel.visible:
			tuning_panel.close_panel()


func _on_help_closed() -> void:
	if _camera:
		_camera.input_enabled = true
	# Don't immediately resume hint — let the cycle timer handle it
	if _help_hint:
		_help_hint.resume_hint()


func _on_tuner_opened() -> void:
	if _camera:
		_camera.input_enabled = false
	# Panel exclusivity — close help if open
	if _help_panel and _help_panel.visible:
		_help_panel.hide_panel()


func _on_tuner_closed() -> void:
	if _camera:
		_camera.input_enabled = true
```

- [ ] **Step 6: Add H/? input handling in `_unhandled_input()`**

In the `_unhandled_input()` method, add H/? handling **before** the ESC block (so it's checked first). Add at the beginning of the method:

```gdscript
	# H or ? — toggle help panel
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode == KEY_H:
			if _help_panel:
				_help_panel.toggle()
				get_viewport().set_input_as_handled()
			return
		elif event.physical_keycode == KEY_SLASH and event.shift_pressed:
			if _help_panel:
				_help_panel.toggle()
				get_viewport().set_input_as_handled()
			return
```

Also update the ESC handling to check HelpPanel first. Before the existing `if event.is_action_pressed("ui_cancel"):` block, add:

```gdscript
	# ESC — close help panel first if open
	if event.is_action_pressed("ui_cancel") and _help_panel and _help_panel.visible:
		_help_panel.hide_panel()
		get_viewport().set_input_as_handled()
		return
```

- [ ] **Step 7: Commit**

```bash
git add src/pmview-app/scripts/HostViewController.gd
git commit -m "Wire HelpPanel and HelpHint in HostViewController

Remove all HudBar references. Add H/? toggle, ESC close, panel
exclusivity, camera suppression via input_enabled flag. Ref #50"
```

---

## Chunk 3: Addon File Sync and Final Verification

### Task 10: Copy new addon files to pmview-app

The addon is developed in `src/pmview-bridge-addon/` but consumed from `src/pmview-app/addons/pmview-bridge/`. The new files need to be present in both locations.

**Files:**
- Check: `src/pmview-app/addons/pmview-bridge/ui/` for existing sync pattern

- [ ] **Step 1: Check how addon files are synced**

Look at `src/pmview-app/addons/` to understand if it's a symlink, a copy, or a git submodule. The git status showed `src/pmview-app/addons/` as untracked, which suggests it may be a local copy or symlink.

```bash
ls -la "src/pmview-app/addons/pmview-bridge/ui/"
```

If it's a symlink, the files are automatically available. If it's a copy, we need to copy the new files over. Follow whatever pattern exists.

- [ ] **Step 2: Ensure new files are accessible**

If the addon directory is a copy, copy over the new files:

```bash
cp src/pmview-bridge-addon/addons/pmview-bridge/ui/help_panel.gd \
   src/pmview-bridge-addon/addons/pmview-bridge/ui/help_panel.tscn \
   src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint.gd \
   src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint.tscn \
   src/pmview-bridge-addon/addons/pmview-bridge/ui/help_group.gd \
   src/pmview-bridge-addon/addons/pmview-bridge/ui/help_hint_entry.gd \
   src/pmview-app/addons/pmview-bridge/ui/
```

Also copy the updated `fly_orbit_camera.gd` and `range_tuning_panel.gd`:

```bash
cp src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd \
   src/pmview-app/addons/pmview-bridge/building_blocks/
cp src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.gd \
   src/pmview-app/addons/pmview-bridge/ui/
```

- [ ] **Step 3: Commit if files were copied**

```bash
git add src/pmview-app/addons/
git commit -m "Sync addon files to pmview-app

Copy new help UI files and updated camera/tuner scripts. Ref #50"
```

---

### Task 11: Full build and test verification

- [ ] **Step 1: Run full CI test suite**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
```

Expected: ALL PASS.

- [ ] **Step 2: Grep for any remaining HudBar references**

```bash
grep -r "HudBar\|hud_bar\|HudBarScene" src/ --include="*.gd" --include="*.cs" --include="*.tscn"
```

Expected: No matches (all references removed).

- [ ] **Step 3: Verify new files exist in addon**

```bash
ls -la src/pmview-bridge-addon/addons/pmview-bridge/ui/help_*.gd \
       src/pmview-bridge-addon/addons/pmview-bridge/ui/help_*.tscn
```

Expected: `help_panel.gd`, `help_panel.tscn`, `help_hint.gd`, `help_hint.tscn`, `help_group.gd`, `help_hint_entry.gd` all present.

---

### Task 12: Documentation update

**Files:**
- Modify: `README.md` — update any HudBar references in feature descriptions
- Modify: `docs/ARCHITECTURE.md` — update UI layer description if HudBar is mentioned

- [ ] **Step 1: Search for HudBar in docs**

```bash
grep -r "HudBar\|hud_bar\|HUD bar" docs/ README.md --include="*.md"
```

- [ ] **Step 2: Update any references found**

Replace HudBar mentions with Help panel description. Update UI layer documentation to describe the new HelpPanel + HelpHint architecture.

- [ ] **Step 3: Commit**

```bash
git add README.md docs/
git commit -m "Update docs — replace HudBar references with Help panel

Ref #50"
```

- [ ] **Step 4: Update design spec status**

Change the spec status from "Draft" to "Implemented":

```bash
git add docs/superpowers/specs/2026-03-19-floating-help-panel-design.md
git commit -m "Mark floating help panel spec as implemented

Closes #50"
```

Note: The final commit message includes "Closes #50" to auto-close the GitHub issue when the PR merges.
