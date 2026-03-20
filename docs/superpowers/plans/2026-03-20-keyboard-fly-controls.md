# Keyboard Fly Controls Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add arrow-key camera look in Fly and Orbit modes, remap archive scrub to modifier+arrow combos.

**Architecture:** Arrow key polling added to `_process_fly()` and `_process_orbit()` in the camera. Scrub handling in HostViewController changed to require Shift minimum. Help panel content strings updated.

**Tech Stack:** GDScript (Godot 4.6). No C# changes, no new files.

**Spec:** `docs/superpowers/specs/2026-03-20-keyboard-fly-controls-design.md`
**GitHub Issue:** #52

---

## Chunk 1: Camera Arrow Key Look + Scrub Remapping

### Task 1: Add arrow-key look to Fly mode

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd`

- [ ] **Step 1: Add `keyboard_look_speed` export**

After line 14 (`@export var transition_speed`), add:

```gdscript
@export var keyboard_look_speed: float = 90.0  ## degrees/second for arrow-key look
```

- [ ] **Step 2: Add arrow-key look polling to `_process_fly()`**

In `_process_fly()`, after the WASD input_dir block (after line 135 `input_dir.y += 1.0`) and before the focus-cancel check, add arrow-key look:

```gdscript
	# Arrow-key look (yaw/pitch)
	var look_input := false
	if Input.is_physical_key_pressed(KEY_LEFT):
		_fly_yaw += deg_to_rad(keyboard_look_speed) * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_RIGHT):
		_fly_yaw -= deg_to_rad(keyboard_look_speed) * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_UP):
		_fly_pitch += deg_to_rad(keyboard_look_speed) * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_DOWN):
		_fly_pitch -= deg_to_rad(keyboard_look_speed) * delta
		look_input = true
	_fly_pitch = clampf(_fly_pitch, -PI / 2.0 + 0.1, PI / 2.0 - 0.1)
```

Then update the existing focus-cancel check to also trigger on look input. Change:

```gdscript
	# Cancel focus if user takes manual control
	if input_dir.length_squared() > 0.0:
		_focus_active = false
```

To:

```gdscript
	# Cancel focus if user takes manual control
	if input_dir.length_squared() > 0.0 or look_input:
		_focus_active = false
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd
git commit -m "Add arrow-key look to Fly mode

LEFT/RIGHT = yaw, UP/DOWN = pitch at keyboard_look_speed deg/s.
Additive with mouse look. Cancels focus animation. Ref #52"
```

---

### Task 2: Add arrow-key look override to Orbit mode

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd`

- [ ] **Step 1: Add orbit look override state variables**

After the existing fly mode state vars (after line 28 `var _is_right_clicking`), add:

```gdscript
# Orbit look override state (arrow-key temporary look)
var _orbit_look_yaw_offset: float = 0.0
var _orbit_look_pitch_offset: float = 0.0
var _orbit_look_timer: float = 0.0
var _orbit_look_easing_back: bool = false
var _orbit_look_ease_progress: float = 0.0
var _orbit_look_ease_start_yaw: float = 0.0
var _orbit_look_ease_start_pitch: float = 0.0
const ORBIT_LOOK_TIMEOUT: float = 10.0
const ORBIT_LOOK_EASE_DURATION: float = 0.5
```

- [ ] **Step 2: Rewrite `_process_orbit()` to support look override**

Replace the existing `_process_orbit()`:

```gdscript
func _process_orbit(delta: float) -> void:
	_orbit_angle += deg_to_rad(orbit_speed) * delta
	position = Vector3(
		orbit_center.x + _radius * cos(_orbit_angle),
		_orbit_height,
		orbit_center.z + _radius * sin(_orbit_angle)
	)
	look_at(orbit_center, Vector3.UP)
```

With:

```gdscript
func _process_orbit(delta: float) -> void:
	_orbit_angle += deg_to_rad(orbit_speed) * delta
	position = Vector3(
		orbit_center.x + _radius * cos(_orbit_angle),
		_orbit_height,
		orbit_center.z + _radius * sin(_orbit_angle)
	)

	# Arrow-key look override
	if input_enabled:
		var arrow_input := false
		if Input.is_physical_key_pressed(KEY_LEFT):
			_orbit_look_yaw_offset += deg_to_rad(keyboard_look_speed) * delta
			arrow_input = true
		if Input.is_physical_key_pressed(KEY_RIGHT):
			_orbit_look_yaw_offset -= deg_to_rad(keyboard_look_speed) * delta
			arrow_input = true
		if Input.is_physical_key_pressed(KEY_UP):
			_orbit_look_pitch_offset += deg_to_rad(keyboard_look_speed) * delta
			arrow_input = true
		if Input.is_physical_key_pressed(KEY_DOWN):
			_orbit_look_pitch_offset -= deg_to_rad(keyboard_look_speed) * delta
			arrow_input = true
		_orbit_look_pitch_offset = clampf(
			_orbit_look_pitch_offset, -PI / 2.0 + 0.1, PI / 2.0 + 0.1)

		if arrow_input:
			_orbit_look_timer = 0.0
			_orbit_look_easing_back = false
		elif _orbit_look_yaw_offset != 0.0 or _orbit_look_pitch_offset != 0.0:
			_orbit_look_timer += delta
			if _orbit_look_timer >= ORBIT_LOOK_TIMEOUT and not _orbit_look_easing_back:
				_orbit_look_easing_back = true
				_orbit_look_ease_progress = 0.0
				_orbit_look_ease_start_yaw = _orbit_look_yaw_offset
				_orbit_look_ease_start_pitch = _orbit_look_pitch_offset

	# Ease back to centre
	if _orbit_look_easing_back:
		_orbit_look_ease_progress += delta / ORBIT_LOOK_EASE_DURATION
		var t := _ease_in_out(_orbit_look_ease_progress)
		_orbit_look_yaw_offset = lerpf(_orbit_look_ease_start_yaw, 0.0, t)
		_orbit_look_pitch_offset = lerpf(_orbit_look_ease_start_pitch, 0.0, t)
		if _orbit_look_ease_progress >= 1.0:
			_orbit_look_yaw_offset = 0.0
			_orbit_look_pitch_offset = 0.0
			_orbit_look_easing_back = false

	# Apply look direction
	if _orbit_look_yaw_offset == 0.0 and _orbit_look_pitch_offset == 0.0:
		look_at(orbit_center, Vector3.UP)
	else:
		# Start from the base look-at direction, apply offsets
		look_at(orbit_center, Vector3.UP)
		var base_basis := global_transform.basis
		var offset_basis := Basis.from_euler(Vector3(
			_orbit_look_pitch_offset, _orbit_look_yaw_offset, 0.0))
		global_transform.basis = base_basis * offset_basis
```

- [ ] **Step 3: Reset orbit look offsets on mode switch**

In `_toggle_mode()`, when switching FROM orbit to fly (inside `Mode.ORBIT:` case), add a reset before changing mode:

```gdscript
		Mode.ORBIT:
			# Reset any orbit look override
			_orbit_look_yaw_offset = 0.0
			_orbit_look_pitch_offset = 0.0
			_orbit_look_easing_back = false
			# Orbit -> Fly: instant, capture current orientation
			_mode = Mode.FLY
```

- [ ] **Step 4: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd
git commit -m "Add arrow-key look override to Orbit mode

Temporary yaw/pitch offset while orbiting. Eases back to look_at
centre after 10s of no arrow input. Ref #52"
```

---

### Task 3: Remap archive scrub controls

**Files:**
- Modify: `src/pmview-app/scripts/HostViewController.gd:198-225`

- [ ] **Step 1: Replace the KEY_LEFT and KEY_RIGHT handlers**

In `_unhandled_input()`, replace the existing `KEY_LEFT` block (lines 204-214):

```gdscript
			KEY_LEFT:
				get_viewport().set_input_as_handled()
				if _poller:
					var step := _poll_interval_seconds
					if event.ctrl_pressed and event.shift_pressed:
						step = 60.0
					elif event.shift_pressed:
						step = 5.0
					_poller.StepPlayback(step, -1)
				if _time_control:
					_time_control.notify_scrub()
```

With:

```gdscript
			KEY_LEFT:
				if event.shift_pressed:
					get_viewport().set_input_as_handled()
					if _poller:
						var step := 15.0
						if event.alt_pressed and event.ctrl_pressed:
							step = 300.0  # 5 minutes
						elif event.ctrl_pressed:
							step = 60.0
						_poller.StepPlayback(step, -1)
					if _time_control:
						_time_control.notify_scrub()
```

And replace the existing `KEY_RIGHT` block (lines 215-225) with the same pattern:

```gdscript
			KEY_RIGHT:
				if event.shift_pressed:
					get_viewport().set_input_as_handled()
					if _poller:
						var step := 15.0
						if event.alt_pressed and event.ctrl_pressed:
							step = 300.0  # 5 minutes
						elif event.ctrl_pressed:
							step = 60.0
						_poller.StepPlayback(step, 1)
					if _time_control:
						_time_control.notify_scrub()
```

Key change: the entire block is now guarded by `if event.shift_pressed:`. Bare arrow keys fall through and are NOT consumed — they reach the camera's `_process()` polling.

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/scripts/HostViewController.gd
git commit -m "Remap archive scrub to require Shift modifier

Shift+arrows=±15s, Ctrl+Shift=±1m, Option+Ctrl+Shift=±5m.
Bare arrows freed for camera look. Ref #52"
```

---

### Task 4: Update Help panel content

**Files:**
- Modify: `src/pmview-app/scripts/HostViewController.gd:239-272`

- [ ] **Step 1: Add arrow-key entry to Camera group**

In `_setup_help_content()`, add a new entry to the camera group after the "Right-click" entry:

```gdscript
		HelpGroup.HelpEntry.create("← → ↑ ↓", "Look around (arrow keys)"),
```

- [ ] **Step 2: Update Archive Mode Playback group**

Replace the scrub entries:

Old:
```gdscript
		HelpGroup.HelpEntry.create("← →", "Scrub (poll interval)"),
		HelpGroup.HelpEntry.create("⇧ ← →", "Scrub ±5 seconds"),
		HelpGroup.HelpEntry.create("⌃⇧ ← →", "Scrub ±1 minute"),
```

New:
```gdscript
		HelpGroup.HelpEntry.create("⇧ ← →", "Scrub ±15 seconds"),
		HelpGroup.HelpEntry.create("⌃⇧ ← →", "Scrub ±1 minute"),
		HelpGroup.HelpEntry.create("⌥⌃⇧ ← →", "Scrub ±5 minutes"),
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/HostViewController.gd
git commit -m "Update Help panel with new arrow-key and scrub bindings

Camera group: add arrow-key look entry.
Archive group: remove bare-arrow scrub, update modifier combos. Ref #52"
```

---

### Task 5: Update spec status and close issue

- [ ] **Step 1: Mark spec as implemented**

Change `**Status:** Draft` to `**Status:** Implemented` in `docs/superpowers/specs/2026-03-20-keyboard-fly-controls-design.md`.

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-03-20-keyboard-fly-controls-design.md
git commit -m "Mark keyboard fly controls spec as implemented

Closes #52"
```
