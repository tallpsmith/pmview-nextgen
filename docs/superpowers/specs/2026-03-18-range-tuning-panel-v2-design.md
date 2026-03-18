# Range Tuning Panel v2 + HUD Bar Design Spec

**Date:** 2026-03-18
**Status:** Approved
**Supersedes:** Range tuning panel slider UI from v1 (Tasks 9–11 of `2026-03-17-range-tuning-panel.md`)

---

## Goal

Replace the v1 slider-based range tuning panel with a preset-button modal and add a persistent keybinding HUD bar. The tuner is a "set once for your hardware" calibration tool — users pick presets matching their disk and network hardware, shapes rescale immediately, and they close the modal.

## Components

### 1. Nano-Style HUD Bar

A persistent, always-visible bar at the bottom edge of the viewport showing available keybindings.

**Layout:** Single row, centred, semi-transparent dark background.

```
TAB Mode  |  WASD Move  |  Q/E Elevation  |  F1 Tuner  |  ESC Menu
```

**Behaviour:**
- Always visible in the host view (both orbit and fly modes).
- Keys colour-coded (amber), descriptions muted grey.
- F1 entry highlighted (brighter text, subtle background tint) when tuner modal is open.
- Small font, minimal vertical height — should not obscure the 3D scene.
- `mouse_filter = MOUSE_FILTER_IGNORE` — does not intercept mouse events.

**Implementation:** GDScript `CanvasLayer` → `PanelContainer` with an `HBoxContainer` of `Label` nodes. Lives in the addon at `addons/pmview-bridge/ui/hud_bar.gd` + `.tscn`.

### 2. Range Tuning Modal

A centred modal overlay toggled by F1, containing preset buttons for disk and network hardware speeds.

**Layout:** Horizontal three-column layout. Each column is a zone:

| Disk Total | Per-Disk | Network |
|---|---|---|
| HDD — 150 MB/s | HDD — 150 MB/s | 1 Gbit — 125 MB/s |
| **SATA SSD — 550 MB/s** | **SATA SSD — 550 MB/s** | 10 Gbit — 1.2 GB/s |
| NVMe Gen3 — 3.5 GB/s | NVMe Gen3 — 3.5 GB/s | 25 Gbit — 3.1 GB/s |
| NVMe Gen4 — 7 GB/s | NVMe Gen4 — 7 GB/s | 40 Gbit — 5 GB/s |
| NVMe Gen5 — 14 GB/s | NVMe Gen5 — 14 GB/s | 100 Gbit — 12.5 GB/s |

Bold = default (SATA SSD for disk zones, 1 Gbit for network).

**Visual design:**
- Semi-transparent dark overlay (`rgba(0,0,0,0.35)`) — scene animates behind it.
- Modal panel: dark background (`rgba(22,22,40,0.95)`), rounded corners, subtle border.
- Each zone column: colour-coded background tint and border (amber for Disk Total, green for Per-Disk, blue for Network).
- Preset buttons: label left-aligned, speed right-aligned. Active preset highlighted with zone colour background + border.
- Header row: "Range Tuning" title left, "F1 or ESC to close" hint right.

**Zone name mapping:** The column display labels differ from the C# API zone names. Each column stores the API zone name(s) internally:

| Column Label | API Zone Name(s) | Colour |
|---|---|---|
| Disk Total | `"Disk"` | Amber |
| Per-Disk | `"Per-Disk"` | Green |
| Network | `"Network In"` + `"Network Out"` | Blue |

The Network column applies presets to **both** `"Network In"` and `"Network Out"` simultaneously (same behaviour as v1).

**Behaviour:**
- **Toggle:** F1 opens/closes. ESC also closes (but does not open).
- **Instant apply:** Clicking a preset immediately calls `SceneBinder.UpdateSourceRangeMax(zoneName, presetBytes)`. For the Network column, this calls `UpdateSourceRangeMax` twice — once for `"Network In"` and once for `"Network Out"`. No Apply/Cancel buttons.
- **Active highlight:** The currently-active preset per zone stays highlighted. On first open, the default preset is highlighted (defaults aligned to match a preset — see section 3).
- **Camera auto-focus:** When a preset is clicked, the camera smoothly pans to the clicked zone's shapes so the user sees the rescaling effect. The focus target comes from `SceneBinder.GetZoneCentroid(zoneName)` — see section 5.
- **Zone visibility:** Columns for zones not present in the active scene are hidden (based on keys present in `GetSourceRanges()`). For example, if the host has no per-device disk metrics, the Per-Disk column is not shown. The Network column is shown if either `"Network In"` or `"Network Out"` is present.
- **Input handling:** The modal uses `_unhandled_input()` for F1/ESC key detection. When open, it sets `mouse_filter = MOUSE_FILTER_STOP` on the overlay to prevent mouse events reaching the 3D camera. When closed, the overlay is hidden and mouse events pass through normally.
- **No scene pause:** The 3D scene and MetricPoller continue running while the modal is open.

**Preset definitions (same as v1, kept in GDScript constants):**

Disk presets:
- HDD: 150,000,000
- SATA SSD: 550,000,000
- NVMe Gen3: 3,500,000,000
- NVMe Gen4: 7,000,000,000
- NVMe Gen5: 14,000,000,000

Network presets:
- 1 Gbit: 125,000,000
- 10 Gbit: 1,250,000,000
- 25 Gbit: 3,125,000,000
- 40 Gbit: 5,000,000,000
- 100 Gbit: 12,500,000,000

**Implementation:** GDScript `PanelContainer` with programmatically-built preset buttons. Lives in the addon at `addons/pmview-bridge/ui/range_tuning_panel.gd` + `.tscn` (replaces v1 files).

### 3. SharedZones Default Alignment

Align `SourceRangeMax` defaults in `SharedZones.cs` to match preset values so the tuner opens with a preset already highlighted:

| Zone | Current Default | New Default | Matches Preset |
|---|---|---|---|
| Disk Total (`DiskTotalsZone`) | 500,000,000 | 550,000,000 | SATA SSD |
| Per-Disk (`PerDiskZone`) | 500,000 | 550,000,000 | SATA SSD |
| Network In (`NetworkInZone`) | 125,000,000 | 125,000,000 | 1 Gbit (no change) |
| Network Out (`NetworkOutZone`) | 125,000,000 | 125,000,000 | 1 Gbit (no change) |

**Note:** The Per-Disk default jumps from 500 KB/s to 550 MB/s (1100x increase). The old value of 500,000 bytes/s was a bug — per-device SATA throughput is ~550 MB/s, not 500 KB/s. This is an intentional correction.

### 4. Determining Active Preset

When the tuner opens, `GetSourceRanges()` returns the current `SourceRangeMax` per zone. The panel matches the value against preset definitions:
- Exact match → highlight that preset.
- No match → no preset highlighted (possible if a future "custom" feature is added, but not expected with aligned defaults).

### 5. Camera Auto-Focus

**New SceneBinder API:** Add `GetZoneCentroid(string zoneName) -> Vector3` to `SceneBinder.cs`. This method iterates `_activeBindings`, collects the `GlobalPosition` of all `Node3D` nodes whose binding has the given `ZoneName`, and returns the average position. Returns `Vector3.Zero` if no matching bindings exist. This keeps zone-to-node knowledge inside SceneBinder where it belongs — the GDScript panel just calls the method.

When a preset button is clicked:
1. Determine the API zone name from the button's parent column (e.g. `"Disk"`, not "Disk Total").
2. Call `SceneBinder.GetZoneCentroid(zoneName)` to get the spatial centre of that zone's shapes.
3. Call `fly_orbit_camera.focus_on_position(centroid)` to smoothly pan the camera.

**New camera method:** Add `focus_on_position(target: Vector3)` to `fly_orbit_camera.gd`. This is new code — the existing fly-orbit transition interpolates position, but focus needs orientation-only interpolation in fly mode.

**Orbit mode:** Sets `orbit_center` to the target. The existing `_process_orbit()` loop picks up the new centre naturally.
**Fly mode:** Computes target yaw/pitch from current position toward the target, then interpolates `_fly_yaw` and `_fly_pitch` over ~0.5 seconds using a tween or delta-based easing in `_process_fly()`. No position change — just rotation.

## What This Replaces

The v1 range tuning panel (slider-based, always-visible, Apply button workflow) is replaced entirely:
- `range_tuning_panel.gd` — rewritten with preset buttons
- `range_tuning_panel.tscn` — rewritten with horizontal column layout
- Slider-related code removed (log scale transforms, snap-to-preset, `_snapping` guard)
- Apply button removed — instant apply on preset click

The SceneBinder C# API (`UpdateSourceRangeMax`, `GetSourceRanges`, `BindingsReady`, `IsBound`) is unchanged — the panel just calls these methods differently.

## What's NOT in Scope

- Standalone camera focus hotkeys (1/2/3 for zone groups) — future GitHub issue
- Custom value text entry — future enhancement if presets prove insufficient
- Slider fallback — removed, presets are the only input method

## Tech Notes

- **Input priority:** F1 must be handled in `_unhandled_input()` so it doesn't conflict with GUI controls. When the modal is open and receives F1/ESC, it consumes the event with `get_viewport().set_input_as_handled()`.
- **ESC event ordering:** The tuner modal lives inside a `CanvasLayer` which is a child of the scene root. `HostViewController` (which handles double-tap ESC) is the scene's root script. In Godot's `_unhandled_input()` propagation, children are processed before parents, so the modal sees ESC first and can consume it before `HostViewController`. This is the correct ordering — no special priority configuration needed.
- **ESC context:** When tuner is open, ESC closes tuner (consumed, does not reach `HostViewController`). When tuner is closed/hidden, ESC falls through to the existing double-tap-to-menu behaviour in `HostViewController`.
- **Mouse focus:** The modal overlay blocks mouse events to the 3D viewport when open. When closed, mouse events pass through for camera control. No GUI focus is set on the overlay — keyboard events flow through `_unhandled_input()`, not GUI focus.
