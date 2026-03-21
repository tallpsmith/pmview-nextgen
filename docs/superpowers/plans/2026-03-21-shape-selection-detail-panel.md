# Shape Selection & Detail Panel Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add click-to-select shapes with visual highlighting (outline + emissive glow), camera fly-to, and a live 2D detail panel showing metric bindings grouped by property.

**Architecture:** Raycasting via StaticBody3D collision on shapes, highlight via emission + inverted-hull outline shader, SceneBinder API exposes bindings per node as Godot Dictionary, 2D Control panel on CanvasLayer, HostViewController orchestrates selection state and deselection rules.

**Tech Stack:** GDScript (Godot 4.6), C# (.NET 8.0), gdUnit4 for C# tests, Godot shaders

**Spec:** `docs/superpowers/specs/2026-03-21-shape-selection-detail-panel-design.md`

---

## Chunk 1: Collision Infrastructure + Highlight Shader

### Task 1: Add collision shapes to GroundedBar and GroundedCylinder scenes

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_bar.tscn`
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_cylinder.tscn`

No TDD for this task — it's scene file modifications, not code logic.

- [ ] **Step 1: Add StaticBody3D + BoxShape3D to grounded_bar.tscn**

Add a StaticBody3D child of the GroundedBar root node (sibling of MeshInstance3D), with a CollisionShape3D child using a BoxShape3D sized to match the mesh (0.8 × 1.0 × 0.8). The StaticBody3D must be on collision layer 2 (bit 1) and collision mask 0 (it doesn't need to detect anything, only be detected). Position at y=0.5 to match the MeshInstance3D offset.

The `.tscn` should look like:

```
[gd_scene load_steps=4 format=3]

[ext_resource type="Script" path="res://addons/pmview-bridge/building_blocks/grounded_shape.gd" id="1"]

[sub_resource type="BoxMesh" id="1"]
size = Vector3(0.8, 1, 0.8)

[sub_resource type="BoxShape3D" id="2"]
size = Vector3(0.8, 1, 0.8)

[node name="GroundedBar" type="Node3D"]
script = ExtResource("1")

[node name="MeshInstance3D" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.5, 0)
mesh = SubResource("1")

[node name="StaticBody3D" type="StaticBody3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.5, 0)
collision_layer = 2
collision_mask = 0

[node name="CollisionShape3D" type="CollisionShape3D" parent="StaticBody3D"]
shape = SubResource("2")
```

- [ ] **Step 2: Add StaticBody3D + CylinderShape3D to grounded_cylinder.tscn**

Same pattern but with CylinderShape3D (radius 0.4, height 1.0):

```
[gd_scene load_steps=4 format=3]

[ext_resource type="Script" path="res://addons/pmview-bridge/building_blocks/grounded_shape.gd" id="1"]

[sub_resource type="CylinderMesh" id="1"]
top_radius = 0.4
bottom_radius = 0.4
height = 1.0

[sub_resource type="CylinderShape3D" id="2"]
radius = 0.4
height = 1.0

[node name="GroundedCylinder" type="Node3D"]
script = ExtResource("1")

[node name="MeshInstance3D" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.5, 0)
mesh = SubResource("1")

[node name="StaticBody3D" type="StaticBody3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.5, 0)
collision_layer = 2
collision_mask = 0

[node name="CollisionShape3D" type="CollisionShape3D" parent="StaticBody3D"]
shape = SubResource("2")
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_bar.tscn \
       src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_cylinder.tscn
git commit -m "Add collision shapes to GroundedBar/Cylinder for click raycasting"
```

---

### Task 2: Create highlight outline shader

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/highlight.gdshader`

No TDD — shader code, tested visually by the user in Godot.

- [ ] **Step 1: Create the inverted-hull outline shader**

This shader renders only back-faces (using `FRONT_FACING` built-in) in a solid colour. When applied to a slightly-scaled-up duplicate mesh, it creates a visible outline around the original shape.

```gdshader
shader_type spatial;
render_mode unshaded, cull_front;

uniform vec4 outline_colour : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	ALBEDO = outline_colour.rgb;
	ALPHA = outline_colour.a;
}
```

Key points:
- `cull_front` means only back faces are rendered — this IS the inverted hull technique
- `unshaded` so it's a flat colour unaffected by lighting
- `outline_colour` uniform is configurable (defaults to white)

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/highlight.gdshader
git commit -m "Add outline shader — inverted hull technique for shape selection highlight"
```

---

### Task 3: Add highlight API to GroundedShape

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_shape.gd`

No automated TDD — GDScript in the addon, tested via Godot runtime. But the logic is simple enough to verify by reading.

- [ ] **Step 1: Add outline mesh creation and highlight toggle to GroundedShape**

Add to `grounded_shape.gd`:

1. A `_highlighted: bool = false` state variable
2. A `var _outline_mesh: MeshInstance3D = null` reference
3. A `highlight(enabled: bool)` function that:
   - If enabling and not already highlighted:
     - Sets emission on the existing material: `emission_enabled = true`, `emission = albedo_color`, `emission_energy_multiplier = 2.0`
     - Creates the outline mesh (if not yet created): duplicates the child MeshInstance3D, scales it to 1.05x, applies a ShaderMaterial loaded from `highlight.gdshader`, adds it as a child
     - Shows the outline mesh
     - Sets `_highlighted = true`
   - If disabling and currently highlighted:
     - Reverts emission: `emission_enabled = false`
     - Hides the outline mesh
     - Sets `_highlighted = false`
4. An `is_highlighted() -> bool` getter

The outline mesh is created lazily on first highlight — no cost for shapes that are never selected.

```gdscript
# At the top of grounded_shape.gd, after existing vars:
var _highlighted: bool = false
var _outline_mesh: MeshInstance3D = null

func highlight(enabled: bool) -> void:
	if enabled == _highlighted:
		return
	_highlighted = enabled
	var mesh_instance := _find_mesh_instance()
	if mesh_instance == null:
		return
	var mat := mesh_instance.get_surface_override_material(0)
	if mat is StandardMaterial3D:
		if enabled:
			mat.emission_enabled = true
			mat.emission = mat.albedo_color
			mat.emission_energy_multiplier = 2.0
			_show_outline(mesh_instance)
		else:
			mat.emission_enabled = false
			_hide_outline()

func is_highlighted() -> bool:
	return _highlighted

func _show_outline(source_mesh: MeshInstance3D) -> void:
	if _outline_mesh == null:
		_outline_mesh = MeshInstance3D.new()
		_outline_mesh.mesh = source_mesh.mesh
		var shader := load("res://addons/pmview-bridge/building_blocks/highlight.gdshader")
		var mat := ShaderMaterial.new()
		mat.shader = shader
		_outline_mesh.set_surface_override_material(0, mat)
		_outline_mesh.scale = Vector3(1.05, 1.05, 1.05)
		source_mesh.add_child(_outline_mesh)
	_outline_mesh.visible = true

func _hide_outline() -> void:
	if _outline_mesh != null:
		_outline_mesh.visible = false
```

Note: the outline mesh is added as a child of the MeshInstance3D (which is at y=0.5), so it inherits the same transform. The 1.05x scale is relative to the mesh, creating the outline effect.

- [ ] **Step 2: Verify _apply_colour preserves highlight emission state**

When `_apply_colour()` is called (e.g., by a metric colour binding update), it currently sets `mat.albedo_color`. If the shape is highlighted, we need to also update the emission colour to match the new albedo. Add to the end of `_apply_colour()`:

```gdscript
		# Keep emission in sync if highlighted
		if _highlighted and not ghost:
			mat.emission = colour
```

This goes inside the `if mat is StandardMaterial3D:` block, after the existing ghost/non-ghost colour logic.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_shape.gd
git commit -m "Add highlight API to GroundedShape — emission glow + outline mesh"
```

---

### Task 4: Add highlight delegation to StackGroupNode

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/stack_group_node.gd`

- [ ] **Step 1: Add highlight function that delegates to all children**

Add to `stack_group_node.gd`:

```gdscript
func highlight(enabled: bool) -> void:
	for child in get_children():
		if child.has_method("highlight"):
			child.highlight(enabled)

func is_highlighted() -> bool:
	for child in get_children():
		if child.has_method("is_highlighted"):
			return child.is_highlighted()
	return false
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/stack_group_node.gd
git commit -m "Add highlight delegation to StackGroupNode — highlights all child segments"
```

---

## Chunk 2: SceneBinder API — GetBindingsForNode

### Task 5: Add LastRawValue tracking to ActiveBinding and store raw values during ApplyMetrics

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs`
- Test: `src/pmview-bridge-addon/test/SceneBinderTests.cs`

- [ ] **Step 1: Write failing test — raw value stored after ApplyMetrics**

Add to `SceneBinderTests.cs`:

```csharp
[TestCase]
[RequireGodotRuntime]
public async Task GetBindingsForNode_ReturnsBindingsGroupedByProperty()
{
    var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
    var node3D = (Node3D)runner.Scene();
    var binder = new SceneBinder();
    runner.Scene().AddChild(binder);

    var bindable = new PcpBindable();
    var heightBinding = new PcpBindingResource
    {
        MetricName = "kernel.cpu.user",
        TargetProperty = "height",
        SourceRangeMin = 0f, SourceRangeMax = 100f,
        TargetRangeMin = 0.2f, TargetRangeMax = 5.0f,
        InitialValue = 0f,
        ZoneName = "CPU",
        InstanceName = "cpu0"
    };
    bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { heightBinding };
    node3D.AddChild(bindable);
    binder.BindFromSceneProperties(node3D);

    // Apply a metric value so LastRawValue is populated
    var metrics = new Godot.Collections.Dictionary
    {
        ["kernel.cpu.user"] = new Godot.Collections.Dictionary
        {
            ["instances"] = new Godot.Collections.Dictionary { [7] = 42.3 },
            ["name_to_id"] = new Godot.Collections.Dictionary { ["cpu0"] = 7 }
        }
    };
    binder.ApplyMetrics(metrics);

    var result = binder.GetBindingsForNode(node3D);
    AssertThat(result.ContainsKey("zone")).IsTrue();
    AssertThat(result["zone"].AsString()).IsEqual("CPU");
    AssertThat(result.ContainsKey("instance")).IsTrue();
    AssertThat(result["instance"].AsString()).IsEqual("cpu0");
    AssertThat(result.ContainsKey("properties")).IsTrue();

    var properties = result["properties"].AsGodotDictionary();
    AssertThat(properties.ContainsKey("height")).IsTrue();

    var heightBindings = properties["height"].AsGodotArray();
    AssertThat(heightBindings.Count).IsEqual(1);

    var entry = heightBindings[0].AsGodotDictionary();
    AssertThat(entry["metric"].AsString()).IsEqual("kernel.cpu.user");
    AssertThat(entry["value"].AsDouble()).IsEqualApprox(42.3, 0.01);

    await runner.AwaitIdleFrame();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln` — should fail because `GetBindingsForNode` doesn't exist yet.

- [ ] **Step 3: Write failing test — no bindings returns empty dictionary**

```csharp
[TestCase]
[RequireGodotRuntime]
public void GetBindingsForNode_NoBindings_ReturnsEmptyDict()
{
    var binder = new SceneBinder();
    var node = new Node3D();
    var result = binder.GetBindingsForNode(node);
    AssertThat(result.Count).IsEqual(0);
}
```

- [ ] **Step 4: Write failing test — ghost shape returns null value**

```csharp
[TestCase]
[RequireGodotRuntime]
public async Task GetBindingsForNode_NoMetricsApplied_ReturnsNullValue()
{
    var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
    var node3D = (Node3D)runner.Scene();
    var binder = new SceneBinder();
    runner.Scene().AddChild(binder);

    var bindable = new PcpBindable();
    var binding = new PcpBindingResource
    {
        MetricName = "kernel.cpu.user",
        TargetProperty = "height",
        SourceRangeMin = 0f, SourceRangeMax = 100f,
        TargetRangeMin = 0.2f, TargetRangeMax = 5.0f,
        InitialValue = 0f,
        ZoneName = "CPU",
        InstanceName = "cpu0"
    };
    bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { binding };
    node3D.AddChild(bindable);
    binder.BindFromSceneProperties(node3D);

    // Don't call ApplyMetrics — simulates ghost/missing data
    var result = binder.GetBindingsForNode(node3D);
    var properties = result["properties"].AsGodotDictionary();
    var heightEntries = properties["height"].AsGodotArray();
    var entry = heightEntries[0].AsGodotDictionary();

    // Value should be Variant.Type.Nil when no raw value has been received
    AssertThat(entry["value"].VariantType).IsEqual(Variant.Type.Nil);

    await runner.AwaitIdleFrame();
}
```

- [ ] **Step 5: Implement — add LastRawValue tracking and GetBindingsForNode**

In `SceneBinder.cs`:

1. Add a `Dictionary<ActiveBinding, double?>` field to track last raw values:
```csharp
private readonly Dictionary<ActiveBinding, double?> _lastRawValues = new();
```

2. In `ApplyMetrics()`, after `ExtractValue()` succeeds (line ~186), store the raw value:
```csharp
_lastRawValues[active] = rawValue;
```

3. When `ActiveBinding` is replaced (e.g., in `UpdateSourceRangeMax`), migrate the raw value too.

4. Clear `_lastRawValues` in `UnloadCurrentScene()` and `BindFromSceneProperties()`.

5. Add the public method:
```csharp
public Godot.Collections.Dictionary GetBindingsForNode(Node node)
{
    var result = new Godot.Collections.Dictionary();
    var matchingBindings = _activeBindings.Where(ab => ab.TargetNode == node).ToList();

    if (matchingBindings.Count == 0)
        return result;

    // Use first binding's zone/instance for header
    var firstBinding = matchingBindings[0].Resolved.Binding;
    result["zone"] = firstBinding.ZoneName ?? "";
    result["instance"] = firstBinding.InstanceName ?? firstBinding.InstanceId?.ToString() ?? "";

    var properties = new Godot.Collections.Dictionary();
    foreach (var group in matchingBindings.GroupBy(ab => ab.Resolved.Binding.Property))
    {
        var entries = new Godot.Collections.Array();
        foreach (var ab in group)
        {
            var entry = new Godot.Collections.Dictionary();
            entry["metric"] = ab.Resolved.Binding.Metric;
            entry["value"] = _lastRawValues.TryGetValue(ab, out var raw) && raw.HasValue
                ? Variant.From(raw.Value)
                : default;  // Variant.Type.Nil
            entries.Add(entry);
        }
        properties[group.Key] = entries;
    }
    result["properties"] = properties;

    return result;
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/pmview-bridge-addon/pmview-nextgen.sln --filter "FullyQualifiedName~GetBindingsForNode"`

Expected: all 3 new tests pass.

- [ ] **Step 7: Run full test suite**

Run: `dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"`

Expected: all tests pass (existing + new).

- [ ] **Step 8: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs \
       src/pmview-bridge-addon/test/SceneBinderTests.cs
git commit -m "Add GetBindingsForNode API to SceneBinder — returns bindings grouped by property with raw values"
```

---

## Chunk 3: Detail Panel UI

### Task 6: Create the detail panel

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/detail_panel.gd`
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/detail_panel.tscn`

No automated TDD — GDScript UI, tested visually by the user. Follow the RangeTuningPanel pattern.

- [ ] **Step 1: Create detail_panel.gd**

The panel:
- Extends `Control`
- Signals: `panel_opened`, `panel_closed`
- Anchored top-right, fixed size (~280px wide)
- Has a semi-transparent dark background (matching RangeTuningPanel aesthetic)
- Shows zone + instance header
- Shows metrics grouped by binding property
- Refreshes values when `update_values(bindings_dict: Dictionary)` is called

```gdscript
extends Control

## Detail panel — shows metric bindings and live values for a selected shape.
## Appears top-right when a shape is selected. Non-input-blocking.

signal panel_opened
signal panel_closed

var _header_label: Label = null
var _content_container: VBoxContainer = null
var _value_labels: Dictionary = {}  # metric_name -> Label

func _ready() -> void:
	visible = false
	_build_layout()

func _build_layout() -> void:
	# Panel background
	var panel := PanelContainer.new()
	panel.anchor_left = 1.0
	panel.anchor_right = 1.0
	panel.anchor_top = 0.0
	panel.anchor_bottom = 0.0
	panel.offset_left = -290
	panel.offset_right = -10
	panel.offset_top = 10
	panel.grow_horizontal = Control.GROW_DIRECTION_BEGIN

	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.08, 0.16, 0.92)
	style.border_color = Color(1, 1, 1, 0.15)
	style.set_border_width_all(1)
	style.set_corner_radius_all(6)
	style.set_content_margin_all(12)
	panel.add_theme_stylebox_override("panel", style)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)

	_header_label = Label.new()
	_header_label.add_theme_font_size_override("font_size", 14)
	_header_label.add_theme_color_override("font_color", Color.WHITE)
	vbox.add_child(_header_label)

	var sep := HSeparator.new()
	sep.add_theme_color_override("separator", Color(1, 1, 1, 0.1))
	vbox.add_child(sep)

	_content_container = VBoxContainer.new()
	_content_container.add_theme_constant_override("separation", 2)
	vbox.add_child(_content_container)

	panel.add_child(vbox)
	add_child(panel)

func show_for_shape(bindings_dict: Dictionary) -> void:
	_value_labels.clear()
	for child in _content_container.get_children():
		child.queue_free()

	var zone: String = bindings_dict.get("zone", "")
	var instance: String = bindings_dict.get("instance", "")
	_header_label.text = "%s • %s" % [zone, instance] if instance else zone

	var properties: Dictionary = bindings_dict.get("properties", {})
	for prop_name: String in properties:
		# Property group header
		var prop_label := Label.new()
		prop_label.text = prop_name
		prop_label.add_theme_font_size_override("font_size", 11)
		prop_label.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
		_content_container.add_child(prop_label)

		var entries: Array = properties[prop_name]
		for entry: Dictionary in entries:
			var metric_name: String = entry.get("metric", "")
			var row := _create_metric_row(metric_name, entry.get("value"))
			_content_container.add_child(row)

	visible = true
	panel_opened.emit()

func update_values(bindings_dict: Dictionary) -> void:
	var properties: Dictionary = bindings_dict.get("properties", {})
	for prop_name: String in properties:
		var entries: Array = properties[prop_name]
		for entry: Dictionary in entries:
			var metric_name: String = entry.get("metric", "")
			if _value_labels.has(metric_name):
				var value = entry.get("value")
				_value_labels[metric_name].text = _format_value(value)

func close_panel() -> void:
	visible = false
	_value_labels.clear()
	panel_closed.emit()

func _create_metric_row(metric_name: String, value) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)

	# Short metric name (strip prefix for readability, e.g., "kernel.cpu.user" -> "user")
	var short_name := metric_name.rsplit(".", true, 1)[-1] if "." in metric_name else metric_name
	var name_label := Label.new()
	name_label.text = "  " + short_name
	name_label.add_theme_font_size_override("font_size", 12)
	name_label.add_theme_color_override("font_color", Color(0.65, 0.65, 0.65))
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(name_label)

	var value_label := Label.new()
	value_label.text = _format_value(value)
	value_label.add_theme_font_size_override("font_size", 12)
	value_label.add_theme_color_override("font_color", Color.WHITE)
	value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	row.add_child(value_label)

	_value_labels[metric_name] = value_label
	return row

func _format_value(value) -> String:
	if value == null:
		return "N/A"
	if value is float:
		return "%.1f" % value
	return str(value)
```

- [ ] **Step 2: Create detail_panel.tscn**

Minimal scene that just instantiates the script on a Control node:

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://addons/pmview-bridge/ui/detail_panel.gd" id="1"]

[node name="DetailPanel" type="Control"]
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
script = ExtResource("1")
```

`mouse_filter = 2` is `MOUSE_FILTER_IGNORE` — panel doesn't consume mouse events.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/detail_panel.gd \
       src/pmview-bridge-addon/addons/pmview-bridge/ui/detail_panel.tscn
git commit -m "Add detail panel — 2D overlay showing metric bindings grouped by property"
```

---

## Chunk 4: Selection Orchestration in HostViewController

### Task 7: Add click-to-select, camera fly-to, and deselection to HostViewController

**Files:**
- Modify: `src/pmview-app/scripts/HostViewController.gd`

This is the integration task — wiring everything together. No TDD (GDScript integration, tested by user in Godot).

- [ ] **Step 1: Add selection state variables**

Add after the existing `var _active_viewpoint_key` (line 28):

```gdscript
var _selected_shape: Node = null  ## Currently selected GroundedShape or StackGroupNode
var _detail_panel: Control = null
```

- [ ] **Step 2: Wire up the detail panel in _ready()**

After the existing `_scene_binder` assignment (line 54), find or create the detail panel:

```gdscript
	# Wire detail panel for shape selection
	_detail_panel = scene.find_child("DetailPanel", true, false)
	if _detail_panel:
		_detail_panel.panel_closed.connect(_on_detail_panel_closed)
```

- [ ] **Step 3: Add left-click raycast handling**

Add a new function and call it from `_unhandled_input()`. Insert early in `_unhandled_input()`, before the existing key handlers (after line 175):

```gdscript
	# Left-click — shape selection via raycast
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_handle_click_selection(event)
		return
```

Then the raycast function:

```gdscript
func _handle_click_selection(event: InputEventMouseButton) -> void:
	if not _camera:
		return
	var from := _camera.project_ray_origin(event.position)
	var dir := _camera.project_ray_normal(event.position)
	var to := from + dir * 1000.0

	var space_state := get_world_3d().direct_space_state
	var query := PhysicsRayQueryParameters3D.create(from, to)
	query.collision_mask = 2  # Layer 2 only
	var result := space_state.intersect_ray(query)

	if result.is_empty():
		if _selected_shape != null:
			_deselect_shape()
			get_viewport().set_input_as_handled()
		# If nothing was selected and nothing was hit, don't consume the click —
		# let other handlers (timeline, etc.) process it.
		return
	var hit_node: Node = result["collider"]
	var shape := _find_selectable_ancestor(hit_node)
	if shape and shape != _selected_shape:
		_select_shape(shape)
		get_viewport().set_input_as_handled()
	elif shape == _selected_shape:
		get_viewport().set_input_as_handled()  # Re-click same shape, no-op but consume
	elif not shape:
		if _selected_shape != null:
			_deselect_shape()
			get_viewport().set_input_as_handled()
```

- [ ] **Step 4: Add ancestor walking to find GroundedShape or StackGroupNode**

```gdscript
func _find_selectable_ancestor(node: Node) -> Node:
	var current := node
	while current != null:
		# If we hit a StackGroupNode, select the whole stack
		if current is StackGroupNode:
			return current
		# If we hit a GroundedShape (bar or cylinder), check if parent is a stack
		if current is GroundedShape:
			if current.get_parent() is StackGroupNode:
				return current.get_parent()
			return current
		current = current.get_parent()
	return null
```

- [ ] **Step 5: Add select and deselect functions**

```gdscript
func _select_shape(shape: Node) -> void:
	# Deselect previous if any
	if _selected_shape and _selected_shape.has_method("highlight"):
		_selected_shape.highlight(false)

	_selected_shape = shape
	shape.highlight(true)

	# Camera: exit orbit, fly to shape
	var target_pos := shape.global_position
	if shape is StackGroupNode:
		# Centre of the stack — average of children positions
		var sum := Vector3.ZERO
		var count := 0
		for child in shape.get_children():
			if child is Node3D:
				sum += child.global_position
				count += 1
		if count > 0:
			target_pos = sum / float(count)

	var orbit_height: float = _camera._orbit_height if _camera else 8.0
	var cam_dir := (_camera.global_position - target_pos).normalized()
	cam_dir.y = 0.0
	if cam_dir.length_squared() < 0.01:
		cam_dir = Vector3(0, 0, 1)
	cam_dir = cam_dir.normalized()
	var camera_pos := target_pos + cam_dir * 8.0
	camera_pos.y = orbit_height

	_camera.fly_to_viewpoint(camera_pos, target_pos)
	_active_viewpoint_key = -1  # Clear any active viewpoint

	# Show detail panel
	if _detail_panel and _scene_binder:
		var bindings: Dictionary = _scene_binder.GetBindingsForNode(shape)
		if shape is StackGroupNode:
			# Merge bindings from all children
			bindings = _get_stack_bindings(shape)
		_detail_panel.show_for_shape(bindings)


func _deselect_shape() -> void:
	if _selected_shape == null:
		return
	if _selected_shape.has_method("highlight"):
		_selected_shape.highlight(false)
	_selected_shape = null
	if _detail_panel and _detail_panel.visible:
		_detail_panel.close_panel()


func _on_detail_panel_closed() -> void:
	# Panel was closed externally — clear selection state
	if _selected_shape and _selected_shape.has_method("highlight"):
		_selected_shape.highlight(false)
	_selected_shape = null
```

- [ ] **Step 6: Add stack bindings merger**

For stacked bars, merge bindings from all child shapes into one dictionary:

```gdscript
func _get_stack_bindings(stack: StackGroupNode) -> Dictionary:
	var zone := ""
	var instance := ""
	var all_properties := {}

	for child in stack.get_children():
		if not child is Node3D:
			continue
		var child_bindings: Dictionary = _scene_binder.GetBindingsForNode(child)
		if child_bindings.is_empty():
			continue
		if zone.is_empty():
			zone = child_bindings.get("zone", "")
			instance = child_bindings.get("instance", "")
		var props: Dictionary = child_bindings.get("properties", {})
		for prop_name: String in props:
			if not all_properties.has(prop_name):
				all_properties[prop_name] = []
			all_properties[prop_name].append_array(props[prop_name])

	return {"zone": zone, "instance": instance, "properties": all_properties}
```

- [ ] **Step 7: Add deselection to ESC priority chain**

In `_unhandled_input()`, add a new ESC handler between "close help panel" (line 198) and "return to orbit from viewpoint" (line 204). Note: RangeTuningPanel handles its own ESC in its `_unhandled_input` and calls `set_input_as_handled()`, so it naturally takes priority before this handler fires — no explicit ordering needed in HostViewController:

```gdscript
	# ESC — deselect shape if selected
	if event.is_action_pressed("ui_cancel") and _selected_shape != null:
		_deselect_shape()
		get_viewport().set_input_as_handled()
		return
```

- [ ] **Step 8: Add deselection to mode-change triggers**

In the existing panel open handlers and viewpoint activation, add deselection:

In `_on_help_opened()` — add `_deselect_shape()` at the start.
In `_on_tuner_opened()` — add `_deselect_shape()` at the start.
In `_activate_viewpoint()` — add `_deselect_shape()` before the fly-to.
For Tab (return to orbit): the camera handles Tab in its own `_unhandled_input`. HostViewController must intercept Tab **before** the click handler to deselect, but NOT consume the event (so the camera still processes it). Insert this at the very top of HostViewController's `_unhandled_input()`, before the H/? key handler (before line 177):

```gdscript
	# Tab — deselect when switching camera mode (don't consume, camera handles toggle)
	if event is InputEventKey and event.pressed and event.physical_keycode == KEY_TAB:
		_deselect_shape()
```

For viewpoint shortcuts (1-4): add `_deselect_shape()` as the first line inside `_activate_viewpoint()`, before the existing early return on line 271.

- [ ] **Step 9: Add live value updates**

Subscribe to MetricPoller's MetricsUpdated signal to refresh the detail panel. In `_ready()`, after finding the poller:

```gdscript
	# Subscribe to metric updates for detail panel refresh
	var metric_poller = scene.find_child("MetricPoller", true, false)
	if metric_poller and metric_poller.has_signal("MetricsUpdated"):
		metric_poller.MetricsUpdated.connect(_on_metrics_updated_for_detail)
```

And the handler:

```gdscript
func _on_metrics_updated_for_detail(_metrics: Dictionary) -> void:
	if _selected_shape == null or _detail_panel == null or not _detail_panel.visible:
		return
	var bindings: Dictionary
	if _selected_shape is StackGroupNode:
		bindings = _get_stack_bindings(_selected_shape)
	else:
		bindings = _scene_binder.GetBindingsForNode(_selected_shape)
	_detail_panel.update_values(bindings)
```

- [ ] **Step 10: Update help content**

In `_setup_help_content()`, add a new entry to the camera or general group:

```gdscript
	HelpGroup.HelpEntry.create("Click", "Select shape (inspect metrics)"),
```

Add this to the `camera_group` entries array.

- [ ] **Step 11: Commit**

```bash
git add src/pmview-app/scripts/HostViewController.gd
git commit -m "Wire shape selection — click raycast, camera fly-to, detail panel, deselection rules"
```

---

### Task 8: Add DetailPanel to RuntimeSceneBuilder

**Files:**
- Modify: `src/pmview-app/scripts/RuntimeSceneBuilder.cs` (or wherever the built scene adds UI nodes)

- [ ] **Step 1: Find where UI nodes are added to the built scene**

Check `RuntimeSceneBuilder.cs` for where HelpPanel, RangeTuningPanel are instantiated and added to the CanvasLayer. The DetailPanel must go on the **same CanvasLayer** — adding a Control directly to a Node3D won't render as 2D UI.

- [ ] **Step 2: Add DetailPanel instantiation**

Find the CanvasLayer node (the same one that HelpPanel and RangeTuningPanel are added to) and add the DetailPanel there:

```csharp
var detailPanelScene = GD.Load<PackedScene>("res://addons/pmview-bridge/ui/detail_panel.tscn");
var detailPanel = detailPanelScene.Instantiate<Control>();
detailPanel.Name = "DetailPanel";
canvasLayer.AddChild(detailPanel);  // Must be the CanvasLayer, not root Node3D
```

Follow the exact pattern used for HelpPanel/RangeTuningPanel in the same file.

- [ ] **Step 3: Also add DetailPanel to TscnWriter if applicable**

Check if `TscnWriter.cs` generates UI nodes in the `.tscn` output. If it does, add a DetailPanel entry on the CanvasLayer. If UI nodes are only added at runtime by RuntimeSceneBuilder, skip this step.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/RuntimeSceneBuilder.cs
git commit -m "Add DetailPanel to runtime scene — enables shape selection UI"
```

---

### Task 9: Run full build and test suite

- [ ] **Step 1: Build everything**

Run: `dotnet build pmview-nextgen.ci.slnf`
Expected: clean build, no errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"`
Expected: all tests pass.

- [ ] **Step 3: Verify no regressions**

Check that existing SceneBinder tests still pass, especially:
- Normalisation tests
- Smooth interpolation tests
- Text binding tests
- Range tuning tests

- [ ] **Step 4: Final commit if any cleanup needed**

Only if build/test revealed issues that need fixing.

---

## Documentation

After all tasks, update the Help panel entry and README if there's a features section. The help content update is handled in Task 7 Step 10.
