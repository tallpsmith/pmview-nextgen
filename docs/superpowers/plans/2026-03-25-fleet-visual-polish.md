# Fleet Visual Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add startup matrix overlay, fix preview animation, and improve preview handoff visuals in fleet mode.

**Architecture:** Three changes to `FleetViewController.gd` (startup matrix, animation wiring, handoff fade) plus one small addition to `holographic_beam.gd` (`dim_to` method). All use existing `MatrixProgressGrid` shader — no new shaders or scenes.

**Tech Stack:** GDScript (Godot 4.6), existing `matrix_progress_grid.gd` shader/script, `holographic_beam.gd`

**Spec:** `docs/superpowers/specs/2026-03-25-fleet-visual-polish-design.md`

**Task ordering rationale:** Tasks 1-2 establish foundations (beam method, startup matrix). Tasks 3-4 build the progressive reveal and early selection on top. Tasks 5-6 handle the preview-specific changes (animation fix, handoff fade). Task 7 verifies everything together. This ordering avoids layering preview handoff changes before the startup matrix lifecycle is established.

---

### Task 1: Add `dim_to()` method to holographic beam

**Files:**
- Modify: `src/pmview-app/scripts/holographic_beam.gd:102-113`

The smallest, most isolated change. The beam already has `fade_in()` — we add a general-purpose `dim_to()` that tweens `global_alpha` from its current value to any target. Used by the preview handoff (Task 6) and also replaces the hardcoded beam fade in `_exit_focus()`.

- [ ] **Step 1: Add `dim_to()` method**

Add after the existing `fade_in()` method (line 113):

```gdscript
## Dim the beam to a target opacity over the given duration.
## Reads the current global_alpha from the shader, so it works from any starting value.
func dim_to(target: float, duration: float = 0.5) -> void:
	var mat: ShaderMaterial = mesh.surface_get_material(0)
	if not mat:
		return
	var current: float = mat.get_shader_parameter("global_alpha")
	var tween := create_tween()
	tween.tween_method(
		func(val: float) -> void: mat.set_shader_parameter("global_alpha", val),
		current, target, duration
	).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/scripts/holographic_beam.gd
git commit -m "Add dim_to() on holographic beam for arbitrary opacity targets"
```

---

### Task 2: Fleet startup matrix overlay — state and spawning

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd:25-40` (new vars), `54-65` (`_ready`), `84-108` (`_build_grid`)

Add the startup matrix state variables, spawn the overlay in `_ready()`, and hide compact hosts on creation. The `_host_sample_counts` dictionary persists until it's no longer needed (NOT cleared on dissolve — Task 4's dimming loop reads it).

- [ ] **Step 1: Add startup matrix state variables**

Add after the existing variable declarations (around line 36):

```gdscript
# Startup matrix overlay state
var _startup_matrix: MeshInstance3D = null
var _host_sample_counts: Dictionary = {}
var _hosts_ready_count: int = 0
## Padding around grid bounds to cover outermost host bezels
const HOST_FOOTPRINT_PADDING := 2.4
```

- [ ] **Step 2: Hide compact hosts on creation**

In `_build_grid()`, add `host_node.set_opacity(0.0)` after `fleet_grid.add_child(host_node)` (after line 101). This must happen after `add_child` because `set_opacity` accesses child mesh materials that are created in `_ready()`:

```gdscript
		fleet_grid.add_child(host_node)
		host_node.set_opacity(0.0)
		_hosts.append(host_node)
```

- [ ] **Step 3: Spawn startup matrix in `_ready()`**

In `_ready()`, add the startup matrix spawn after `_build_grid(hostnames)` (after line 59):

```gdscript
	_build_grid(hostnames)
	_spawn_startup_matrix()
```

Add the method itself:

```gdscript
func _spawn_startup_matrix() -> void:
	_startup_matrix = MeshInstance3D.new()
	_startup_matrix.set_script(MatrixGridScript)
	_startup_matrix.name = "StartupMatrix"
	# Set dimensions BEFORE add_child (which triggers _ready and builds PlaneMesh)
	_startup_matrix.grid_width = _grid_bounds.size.x + HOST_FOOTPRINT_PADDING
	_startup_matrix.grid_depth = _grid_bounds.size.y + HOST_FOOTPRINT_PADDING
	_startup_matrix.position = Vector3(
		_grid_bounds.position.x + _grid_bounds.size.x / 2.0,
		0.1,
		_grid_bounds.position.y + _grid_bounds.size.y / 2.0
	)
	add_child(_startup_matrix)
	print("[FleetView] Startup matrix spawned (%sx%s)" % [
		_startup_matrix.grid_width, _startup_matrix.grid_depth])
```

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd
git commit -m "Spawn startup matrix overlay and hide hosts until data arrives"
```

---

### Task 3: Fleet startup matrix — progressive host reveal

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd:687-698` (`_on_fleet_metrics_updated`)

Track sample counts per host. On second sample, reveal the host with a tween and advance the matrix progress. When all hosts are ready, dissolve the matrix.

- [ ] **Step 1: Add host reveal logic to `_on_fleet_metrics_updated()`**

Add the sample tracking after the existing metric update logic:

```gdscript
func _on_fleet_metrics_updated(hostname: String, metrics: Dictionary) -> void:
	_fleet_update_count += 1
	if _fleet_update_count <= 3 or _fleet_update_count % 10 == 0:
		print("[FleetView] Update #%d — host '%s': %s" % [
			_fleet_update_count, hostname, metrics])
	var host: Node3D = _host_lookup.get(hostname)
	if not host:
		return
	for metric_name: String in metrics:
		host.set_metric_value(metric_name, metrics[metric_name])

	# Track sample counts for startup reveal
	if is_instance_valid(_startup_matrix):
		var count: int = _host_sample_counts.get(hostname, 0) + 1
		_host_sample_counts[hostname] = count
		if count == 2:
			_reveal_host(host)
```

- [ ] **Step 2: Add `_reveal_host()` method**

```gdscript
## Fade a compact host in once its second metric sample arrives.
## The host appears "already running" with meaningful bar heights.
func _reveal_host(host: Node3D) -> void:
	_hosts_ready_count += 1

	# Determine target opacity: 1.0 normally, 0.3 if in focus mode (dimmed)
	var target_opacity := 1.0
	if _view_mode == ViewMode.FOCUS and _focused_host_index >= 0 \
			and host != _hosts[_focused_host_index]:
		target_opacity = 0.3

	var tween := host.create_tween()
	tween.tween_method(
		func(val: float) -> void: host.set_opacity(val),
		0.0, target_opacity, 0.3
	)
	# Snap at end to reset transparency mode (avoids alpha-blend sorting artifacts)
	tween.tween_callback(func() -> void: host.set_opacity(target_opacity))

	# Advance startup matrix progress
	if is_instance_valid(_startup_matrix):
		_startup_matrix.set_progress(float(_hosts_ready_count) / float(_hosts.size()))

	# Check if all hosts are ready
	if _hosts_ready_count >= _hosts.size():
		_dissolve_startup_matrix()
```

- [ ] **Step 3: Add `_dissolve_startup_matrix()` method**

Note: `_host_sample_counts` is NOT cleared here — the dimming loop in `_enter_focus()` (Task 4) reads it to decide which hosts to dim. The dictionary is harmless to keep around (small int values, one per host).

```gdscript
## Dissolve the startup matrix overlay.
## Does NOT clear _host_sample_counts — still needed by the dimming loop.
func _dissolve_startup_matrix() -> void:
	if not is_instance_valid(_startup_matrix):
		return
	_startup_matrix.tree_exiting.connect(func() -> void: _startup_matrix = null)
	_startup_matrix.dissolve(0.8)
	print("[FleetView] Startup matrix dissolving")
```

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd
git commit -m "Reveal hosts progressively as metric data arrives

Each host fades in after its 2nd sample (meaningful rates).
Startup matrix tracks progress and dissolves when all hosts ready."
```

---

### Task 4: Early host selection during startup

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd:205-265` (`_enter_focus`), `412-432` (`_cleanup_focus_state`)

Handle the case where a user selects a host before all hosts have finished their startup reveal. Dissolve startup matrix early, keep un-revealed hosts invisible. Also fix `_cleanup_focus_state()` to be startup-aware.

- [ ] **Step 1: Modify `_enter_focus()` to handle startup matrix**

Add startup matrix dissolution at the beginning of `_enter_focus()`, after the existing cleanup check (after line 208):

```gdscript
func _enter_focus(host_index: int) -> void:
	# Cancel any existing preview/pipeline if re-entering focus
	if _fleet_pipeline or _preview_zones:
		_cleanup_focus_state()
	# Dissolve startup matrix early if still active
	if is_instance_valid(_startup_matrix):
		_dissolve_startup_matrix()
	_focused_host_index = host_index
```

- [ ] **Step 2: Modify the dimming loop to skip un-revealed hosts**

Replace the existing dimming loop (lines 214-216):

**Old code:**
```gdscript
	for i in range(_hosts.size()):
		if i != host_index:
			_hosts[i].set_opacity(0.3)
```

**New code:**
```gdscript
	# Dim other hosts, but skip those that haven't received 2 samples yet
	# (they're still invisible and have no meaningful data to show)
	for i in range(_hosts.size()):
		if i != host_index:
			var h: Node3D = _hosts[i]
			var samples: int = _host_sample_counts.get(h.hostname, 0)
			if samples >= 2:
				h.set_opacity(0.3)
			# else: leave at 0.0 — _reveal_host will set 0.3 when ready
```

- [ ] **Step 3: Make `_cleanup_focus_state()` startup-aware**

The existing `_cleanup_focus_state()` sets ALL hosts to `opacity 1.0`. If the user enters and exits focus during startup (before all hosts are revealed), this would make un-revealed hosts jump to full opacity with garbage data — exactly the glitch we're preventing.

Replace the host opacity restore loop in `_cleanup_focus_state()` (lines 429-430):

**Old code:**
```gdscript
	for host: Node3D in _hosts:
		host.set_opacity(1.0)
```

**New code:**
```gdscript
	# Restore opacity, but only for hosts that have been revealed (2+ samples).
	# Un-revealed hosts stay at 0.0 and continue their normal reveal flow.
	for host: Node3D in _hosts:
		var samples: int = _host_sample_counts.get(host.hostname, 0)
		if samples >= 2:
			host.set_opacity(1.0)
```

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd
git commit -m "Handle early host selection during startup reveal

Dissolve startup matrix early, keep un-revealed hosts invisible
until their data arrives. Cleanup restores only revealed hosts."
```

---

### Task 5: Wire preview animation (MetricPoller → SceneBinder)

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd:336-362`

The animation fix. Add `_wire_preview_animation()` and call it from `_on_preview_build_completed()`. This is independent of the startup matrix changes.

- [ ] **Step 1: Add `_wire_preview_animation()` method**

Add after `_configure_preview_poller()` (around line 403) in `FleetViewController.gd`:

```gdscript
## Connect MetricPoller → SceneBinder in the preview zones so bars animate.
## In full HostView this wiring lives in host_view_controller.gd, but the
## zones-only preview doesn't have that script attached.
func _wire_preview_animation() -> void:
	if not _preview_zones:
		return
	var poller: Node = _preview_zones.find_child("MetricPoller", true, false)
	var binder: Node = _preview_zones.find_child("SceneBinder", true, false)
	if not poller or not binder:
		push_warning("[FleetView] Cannot wire preview animation — missing poller or binder")
		return
	poller.MetricsUpdated.connect(
		func(_hostname: String, metrics: Dictionary) -> void:
			binder.ApplyMetrics(metrics)
	)
	print("[FleetView] Wired preview MetricPoller → SceneBinder")
```

- [ ] **Step 2: Call `_wire_preview_animation()` from `_on_preview_build_completed()`**

In `_on_preview_build_completed()`, add the call after `_configure_preview_poller()` (line 362):

```gdscript
		_configure_preview_poller()
		_wire_preview_animation()
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd
git commit -m "Wire MetricPoller→SceneBinder in fleet preview for live animation"
```

---

### Task 6: Preview handoff fade (matrix dissolve + beam dim)

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd:336-362` (`_on_preview_build_completed`), `268-305` (`_exit_focus`)

Replace the `set_final_opacity(0.9)` call with a sequential dissolve+dim sequence. Null out `_matrix_grid` after dissolve to prevent freed-object access on ESC. Also fix `_exit_focus()` beam fade to use `dim_to()` (reads current alpha instead of hardcoding 1.0 as start value — prevents a visual flash when beam is already at 0.25).

- [ ] **Step 1: Replace `set_final_opacity` with dissolve + beam dim sequence**

In `_on_preview_build_completed()`, replace the existing matrix opacity block (lines 342-343):

**Old code:**
```gdscript
	if _matrix_grid and _matrix_grid.has_method("set_final_opacity"):
		_matrix_grid.set_final_opacity(0.9)
```

**New code:**
```gdscript
	# Dissolve the matrix grid — it's served its loading purpose.
	# Null out the reference via tree_exiting to prevent freed-object access
	# if ESC is pressed after dissolve completes.
	if _matrix_grid and _matrix_grid.has_method("dissolve"):
		_matrix_grid.tree_exiting.connect(func() -> void: _matrix_grid = null)
		_matrix_grid.dissolve(0.8)

	# After matrix dissolves, dim the beam to a whisper so the preview
	# takes centre stage. Sequential: wait for dissolve, then dim.
	if _beam and _beam.has_method("dim_to"):
		var tween := create_tween()
		tween.tween_interval(0.8)  # wait for matrix dissolve
		tween.tween_callback(func() -> void:
			if _beam and _beam.has_method("dim_to"):
				_beam.dim_to(0.25, 0.5)
		)
```

- [ ] **Step 2: Fix `_exit_focus()` beam fade to use `dim_to()`**

The existing beam fade in `_exit_focus()` hardcodes `1.0` as the tween start value (lines 287-293). After the preview handoff dims the beam to 0.25, the ESC exit would snap the beam UP to 1.0 before fading down — a visible flash. Replace with `dim_to()` which reads the current alpha.

**Old code:**
```gdscript
	if _beam and _beam.has_method("fade_in"):
		var mat: ShaderMaterial = _beam.mesh.surface_get_material(0)
		if mat:
			var tween := _beam.create_tween()
			tween.tween_method(
				func(val: float) -> void: mat.set_shader_parameter("global_alpha", val),
				1.0, 0.0, 1.0
			).set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_CUBIC)
```

**New code:**
```gdscript
	if _beam and _beam.has_method("dim_to"):
		_beam.dim_to(0.0, 1.0)
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd
git commit -m "Dissolve matrix and dim beam after preview loads

Preview takes centre stage — matrix fades completely, beam
drops to 0.25 alpha. ESC exit now uses dim_to() to avoid
flash when beam is already dimmed."
```

---

### Task 7: Build verification and manual test checklist

**Files:** None (verification only)

- [ ] **Step 1: Run CI build and tests**

```bash
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
```

Expected: All tests pass. GDScript changes aren't compiled by dotnet but this confirms no C# side-effects.

- [ ] **Step 2: Manual verification checklist (user runs in Godot editor)**

1. **Startup matrix covers grid:** Load fleet with 12+ hosts. Matrix overlay visible, hosts hidden.
2. **Progressive reveal:** Hosts fade in individually after ~2 poll ticks. Bars show real values, not flat/zero.
3. **Matrix dissolves on completion:** Once all hosts visible, matrix fades out over 0.8s.
4. **Early host selection:** Click a visible host before all hosts have loaded. Focus mode works, matrix dissolves, un-revealed hosts stay invisible until data arrives (then appear dimmed at 0.3).
5. **Exit focus during startup:** ESC from focus back to patrol. Revealed hosts restore to 1.0, un-revealed hosts stay at 0.0 and continue their reveal flow.
6. **Preview animation:** Select a host, wait for preview to load. Bars should be breathing/animating, not static.
7. **Preview handoff:** Matrix on beam top dissolves (0.8s), then beam dims to subtle level (0.5s). No brightness competition.
8. **ESC from focus (after preview):** Beam fades smoothly from 0.25 to 0.0 — no flash. No errors in console about freed objects. Camera returns to patrol.
9. **Dive-in:** Press Enter from animated preview. HostView loads with continuous animation, no flicker.
10. **ESC back to fleet:** From HostView (entered via fleet), ESC returns to fleet focus on same host.

- [ ] **Step 3: Commit any fixes from manual testing**

If manual testing reveals issues, fix and commit before pushing.
