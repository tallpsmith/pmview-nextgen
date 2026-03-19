# Floating Help Panel — Design Spec

**Date:** 2026-03-19
**Status:** Implemented
**Replaces:** HudBar (bottom-anchored key label bar)

## Problem

The current HudBar is a persistent horizontal bar at the bottom of the viewport showing key bindings. It's distracting, takes up screen real estate, and doesn't scale well as controls grow. Users who already know the controls don't need it, and new users would benefit from a better-organised reference they can summon on demand.

## Solution

Replace the HudBar with two components:

1. **HelpPanel** — a floating, translucent, centre-screen overlay listing all controls in grouped sections. Toggled with `H`, `?`, or `ESC`.
2. **HelpHint** — a subtle cycling text in the bottom-right corner that rotates through key tips, reminding users the Help panel exists.

## Architecture: Addon / App Split

The **addon** (`pmview-bridge-addon`) provides the reusable mechanism:
- `HelpPanel` — the panel UI node: rendering, toggle logic, open/close animation, group layout
- `HelpHint` — the cycling hint node: timer-driven text rotation, fade in/out
- Both accept content via a structured API (array of groups, each with entries and an `enabled` flag)
- Both nodes live in `addons/pmview-bridge/ui/` alongside the existing `range_tuning_panel`

The **app** (`pmview-app`) provides the content:
- Populates groups with the actual key bindings
- Sets the `enabled` flag per group based on current mode (live vs archive)
- Wires the `H`/`?` input handling in `HostViewController`

This split means any project using the addon gets the Help panel infrastructure for free — they just supply their own control content.

## HelpPanel Design

### Visual
- **Position:** Centre-screen overlay
- **Background:** Dark translucent (`rgba(10, 10, 30, 0.88)`) with backdrop blur
- **Border:** Purple accent (`rgba(130, 80, 220, 0.4)`), 12px border radius — consistent with Range Tuning panel
- **Max width:** ~520px
- **Header:** "CONTROLS" title, "H / ? / ESC to close" subtitle

### Scene Tree Placement
- HelpPanel and HelpHint live on the same `UILayer` CanvasLayer as the existing Range Tuning panel
- HelpPanel should have a higher z-index than TimeControl and RangeTuningPanel so it renders on top of everything
- Injected via `RuntimeSceneBuilder.cs` (runtime path) and `TscnWriter.cs` (projector path), replacing the current HudBar injection points

### Toggle Behaviour
- **Open:** `H` or `?` key press
- **Close:** `H`, `?`, or `ESC`
- Same toggle pattern as F1 (Range Tuning panel)
- When open: disables camera input via a flag (see Input Handling below)
- Does NOT pause archive playback (unlike the tuner — playback continues behind the panel)

### Panel Exclusivity
- **HelpPanel and RangeTuningPanel cannot be open simultaneously.** Opening the Help panel while the tuner is open closes the tuner first. Opening the tuner while Help is open closes Help first.
- This simplifies ESC handling — only one panel can consume ESC at a time.

### Control Groups

Four groups, displayed vertically:

#### 1. Camera
| Key | Action |
|-----|--------|
| TAB | Toggle Orbit / Free Look |
| W A S D | Move (free look mode) |
| Q / E | Descend / Ascend |
| SHIFT | Sprint (hold with movement) |
| Right-click | Look around (hold + drag) |

#### 2. Archive Mode Playback
| Key | Action |
|-----|--------|
| SPACE | Play / Pause |
| ← → | Scrub (poll interval) |
| ⇧ ← → | Scrub ±5 seconds |
| ⌃⇧ ← → | Scrub ±1 minute |
| R | Reset time range |
| Mouse → edge | Show timeline panel |
| Click (timeline) | Jump to time |
| ⇧ Click (timeline) | Set IN / OUT range markers |

**In live mode:** entire group rendered at 30% opacity with "(archive mode only)" label.

#### 3. Panels
| Key | Action |
|-----|--------|
| F1 | Range Tuning |
| F2 | Timeline Navigator |
| H / ? | This help panel |

**In live mode:** F2 row individually greyed out with "(archive)" tag.

#### 4. General
| Key | Action |
|-----|--------|
| ESC × 2 | Return to main menu |

### Group Styling
- Group headers: uppercase, 11px, bold, orange (`#f09020`) for general groups, purple (`#8338ec`) for the archive group
- Key column: monospace, lavender (`#e0d0ff`), bold, 110px fixed width
- Description column: soft white (`rgba(255,255,255,0.7)`)
- Disabled groups/rows: 30% opacity

## HelpHint Design

### Visual
- **Position:** Bottom-right corner, 16px margin
- **Font:** Monospace, 12px, very low opacity (~35% white)
- **Background:** Subtle pill (`rgba(255,255,255,0.05)`), 4px border radius
- Key name in orange accent (50% opacity), description in white (30% opacity)

### Cycling Behaviour
- Rotates through a short list of tips: "H for Help", "TAB Orbit / Free Look"
- Each tip visible for **15 seconds**, then fades out
- After all tips cycle, goes silent for **60 seconds**, then restarts
- Fade transition: ~0.5 second ease-in/ease-out
- **Hidden when HelpPanel is open** — no point advertising the menu you're already looking at
- **On panel close:** reset cycle timer (don't immediately show hint)
- **Hidden when TimeControl panel is revealed** (mouse at right edge in archive mode) to avoid visual overlap in the bottom-right region. Resumes cycling when TimeControl hides.

## What Gets Removed

The HudBar is wired through four systems. All must be updated:

1. **Addon files:** Delete `addons/pmview-bridge/ui/hud_bar.tscn` and `hud_bar.gd`
2. **HostViewController.gd:** Remove all HudBar references — `set_tuner_active()` calls, archive label visibility toggles, space label updates
3. **RangeTuningPanel.gd:** Remove `find_child("HudBar")` calls in `_open_panel()` and `_close_panel()`. The `set_tuner_active()` visual feedback is **dropped** — the tuner already has its own visual state (purple border, modal overlay). No replacement needed.
4. **RuntimeSceneBuilder.cs:** Replace HudBar instantiation with HelpPanel + HelpHint instantiation
5. **TscnWriter.cs:** Replace HudBar node emission with HelpPanel + HelpHint nodes in generated `.tscn` files
6. **TscnWriterTests.cs:** Update the two HudBar-presence assertions to assert HelpPanel + HelpHint presence instead

## Input Handling

### Key Detection
- `H` key: `KEY_H` in `_unhandled_input()` — raw keycode check, consistent with existing patterns in the codebase (camera uses raw keycodes, tuner uses raw keycodes)
- `?` key: `KEY_SLASH` with `event.shift_pressed` — this is `Shift + /` which produces `?` on standard layouts. Cross-layout edge cases are accepted as a known limitation; `H` is the primary shortcut.
- Input handled in `HostViewController._unhandled_input()`, which calls `HelpPanel.toggle()`

### Camera Input Suppression
The camera's `_process()` polls `Input.is_physical_key_pressed()` directly — consuming events via `set_input_as_handled()` has no effect on this polling. Instead:

- `FlyOrbitCamera` exposes a `input_enabled: bool` property (default `true`)
- When `input_enabled` is `false`, `_process()` skips all movement and mouse-look processing
- `HelpPanel` emits `panel_opened` and `panel_closed` signals
- `HostViewController` connects these signals to toggle `camera.input_enabled`
- The same mechanism is used when RangeTuningPanel is open (replacing the current implicit suppression where the tuner's overlay blocks mouse events but WASD still leaks through — this is actually a pre-existing bug we fix for free)

### ESC Priority
1. **HelpPanel** (if visible) → close and consume
2. **RangeTuningPanel** (if visible) → close and consume (existing behaviour)
3. **HostViewController** double-press → return to menu (existing behaviour)

Panel exclusivity (see above) means only one of steps 1–2 can fire.

## Content API (Addon)

### Data Classes

```gdscript
# help_group.gd — Resource class
class_name HelpGroup
extends RefCounted

var group_name: String
var header_color: Color = Color(0.94, 0.56, 0.13)  # orange default
var entries: Array[HelpEntry] = []
var enabled: bool = true

class HelpEntry:
    var key_text: String
    var action_text: String
    var enabled: bool = true  # for per-row greying (e.g. F2 in live mode)
```

```gdscript
# help_hint_entry.gd — Resource class
class_name HelpHintEntry
extends RefCounted

var key_text: String
var action_text: String
```

### HelpPanel Interface

```gdscript
signal panel_opened
signal panel_closed

func set_groups(groups: Array[HelpGroup]) -> void
func set_group_enabled(group_name: String, enabled: bool) -> void
func set_entry_enabled(group_name: String, key_text: String, enabled: bool) -> void
func toggle() -> void
func show_panel() -> void
func hide_panel() -> void
```

`set_groups()` should be called once during scene setup. `set_group_enabled()` and `set_entry_enabled()` can be called at any time (e.g. if mode changes while the panel is open — unlikely but free to support). Default `enabled` state is `true` for all groups and entries.

### HelpHint Interface

```gdscript
signal visibility_changed(is_visible: bool)

func set_hints(hints: Array[HelpHintEntry]) -> void
func start_cycling() -> void
func stop_cycling() -> void
func hide_hint() -> void   # immediate hide (for TimeControl overlap)
func resume_hint() -> void  # resume cycling from where it left off
```

## GitHub Issue

Create a GitHub issue to track this work. The PR will reference it with "Closes #N".
