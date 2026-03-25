# Fleet Visual Polish: Startup Matrix, Preview Handoff & Animation Fix

**Date:** 2026-03-25
**Branch:** `feature/fleet-hostview-preview`
**Status:** Design approved

## Summary

Three visual improvements to the fleet mode experience:

1. **Fleet startup matrix overlay** — a city-wide matrix shader covers the grid while hosts connect, hiding compact nodes until meaningful metric data arrives
2. **Preview handoff fade** — after the HostView preview loads, the matrix dissolves completely and the holographic beam dims, letting the preview take centre stage
3. **Preview animation fix** — wire MetricPoller→SceneBinder in the fleet preview so bars animate immediately, not only after diving into full HostView

## 1. Fleet Startup Matrix Overlay

### Problem

When fleet mode loads with a large host set (e.g. 100 hosts), there's a visible delay while `FleetMetricPoller` discovers series IDs, connects to pmproxy, and begins polling. During this time the compact host bars sit at their mock heights (CPU=0.6, Mem=0.4, Disk=0.3, Net=0.2), then abruptly jump to real values. The first sample also produces nonsense rates for counters (no delta yet), causing a flash of flat bars before the second sample provides meaningful data.

### Design

**Startup matrix overlay:** Stored in a new `_startup_matrix: MeshInstance3D` variable (separate from the existing `_matrix_grid` used for per-host preview matrices). Spawned in `FleetViewController._ready()`, immediately after `_build_grid()`. Set `grid_width` and `grid_depth` properties on the node *before* `add_child()` (which triggers `_ready()` and builds the PlaneMesh from those values). Size: `_grid_bounds.size.x + host_footprint_padding` by `_grid_bounds.size.y + host_footprint_padding` where padding accounts for the outermost bezels (~2.4 units). Position at `Y = 0.1` (just above the ground plane).

**Hidden hosts:** Set `set_opacity(0.0)` on every `CompactHost` immediately after creation in `_build_grid()`. Hosts exist in the scene tree (collision shapes, layout) but are invisible.

**Progress tracking:** Add a `_host_sample_counts: Dictionary` (hostname → int) and `_hosts_ready_count: int` to `FleetViewController`. In `_on_fleet_metrics_updated()`:
1. Increment the sample count for the hostname
2. When a host reaches sample count 2:
   - Tween that `CompactHost` from opacity 0.0 → 1.0 over 0.3s. At tween completion, explicitly call `set_opacity(1.0)` to ensure transparency mode is reset to `TRANSPARENCY_DISABLED` (avoids 100+ hosts lingering in alpha-blend mode with sorting artifacts).
   - Increment `_hosts_ready_count` and update startup matrix progress: `_startup_matrix.set_progress(float(_hosts_ready_count) / float(_hosts.size()))`
3. When all hosts have 2+ samples:
   - Connect to `_startup_matrix.progress_complete` signal to trigger dissolve (0.8s fade, then `queue_free`)
   - Null out `_startup_matrix` reference after dissolve completes
   - Clear tracking state

**Early host selection:** If the user clicks a host that already has 2 samples before startup completes:
- Enter focus mode normally
- Dissolve the startup matrix early, null out `_startup_matrix`
- Hosts with < 2 samples remain at opacity 0.0 (NOT 0.3) during focus — they have no meaningful data to show. The `_enter_focus()` dimming loop must skip hosts where `_host_sample_counts[hostname] < 2`. These hosts continue to reveal normally as their 2nd sample arrives (they'll appear at 0.3 opacity since we're in focus mode, matching the other dimmed hosts).

### Files Modified

- `src/pmview-app/scripts/FleetViewController.gd` — spawn startup matrix, track sample counts, reveal hosts, handle early selection

## 2. Preview Handoff Fade

### Problem

After the HostView preview pipeline completes, the matrix grid drops to `set_final_opacity(0.9)` — barely dimmed — and the holographic beam stays at `global_alpha = 1.0`. Both compete visually with the preview, which should be the focal point.

### Design

**Sequential fade-out after preview build completes** (in `_on_preview_build_completed()`):

| Time    | Action                                                                 |
|---------|------------------------------------------------------------------------|
| T+0.0s  | Matrix begins dissolve (0.8s, ease-in). Preview zones start fading in. |
| T+0.8s  | Matrix fully dissolved, freed. Beam dim begins (0.5s → 0.25 `global_alpha`, ease-out). |
| T+1.3s  | Beam at whisper level. Preview is centre stage.                        |

**Replace `set_final_opacity(0.9)` call** with `dissolve(0.8)` on the matrix grid. After the dissolve completes, tween the beam's `global_alpha` from 1.0 → 0.25 over 0.5s.

**New method on holographic_beam.gd:** Add `dim_to(target: float, duration: float)` — tweens `global_alpha` from current value to target. The existing `fade_in()` handles 0→1; this handles arbitrary dimming.

**Null out `_matrix_grid` after dissolve:** The `dissolve()` method on `MatrixProgressGrid` calls `queue_free` via a tween callback, which frees the node but leaves `_matrix_grid` as a dangling reference. GDScript's `if _matrix_grid` does NOT catch freed objects — you need `is_instance_valid()`. To keep things clean: after calling `dissolve()`, connect to the matrix's `tree_exiting` signal to null out `_matrix_grid`. This prevents both the `_exit_focus()` dissolve call and `_cleanup_focus_state()` `queue_free` from operating on a freed object.

**ESC exit:** The existing exit code checks `if _matrix_grid` before dissolving and freeing. With the null-out above, `_matrix_grid` will be `null` by the time ESC is pressed (the dissolve already completed and freed the node). The check passes safely. The beam dim from 0.25→0.0 works the same as 1.0→0.0.

### Files Modified

- `src/pmview-app/scripts/FleetViewController.gd` — change `_on_preview_build_completed()` fade sequence
- `src/pmview-app/scripts/holographic_beam.gd` — add `dim_to()` method

## 3. Preview Animation Fix

### Problem

The HostView preview bars do not animate. The `MetricPoller` polls and emits `MetricsUpdated` signals, and `SceneBinder` has `ApplyMetrics()` ready to drive visual updates, but nobody connects them. The wiring normally lives in `host_view_controller.gd` which is deliberately excluded in zones-only mode.

### Root Cause

`FleetHostPipeline` builds zones with `ZonesOnly = true`, which calls `HostSceneBuilder.BuildZones()`. This creates MetricPoller and SceneBinder as child nodes but does NOT attach `host_view_controller.gd` (that happens in `AddHostViewUi()`). Without the controller script, `MetricsUpdated` signals are emitted but never connected to `SceneBinder.ApplyMetrics()`.

### Design

**Wire the connection in `FleetViewController._on_preview_build_completed()`**, after the existing `_configure_preview_poller()` call:

```gdscript
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
```

**Why here and not in FleetHostPipeline:** The FleetViewController already owns the preview lifecycle and has `_configure_preview_poller()` right alongside. Keeping the wiring co-located with other preview setup is cleaner.

**On dive-in:** When `graft_host_view_ui()` attaches `host_view_controller.gd`, its `_ready()` creates its own connection. The duplicate connection is harmless — `ApplyMetrics` with the same data is idempotent. The old lambda will be disconnected when the zones are reparented and the fleet scene is freed.

**Note:** The `_hostname` parameter in the lambda will always be the preview host's hostname (the preview MetricPoller is configured for a single host). This is correct — we only need the metrics dictionary.

### Files Modified

- `src/pmview-app/scripts/FleetViewController.gd` — add `_wire_preview_animation()`, call it from `_on_preview_build_completed()`

## Testing Strategy

### Unit Tests (where applicable)

- **Sample counting logic:** Verify hosts are revealed at sample 2, not sample 1. Verify progress calculation.
- **Holographic beam `dim_to()`:** Verify shader parameter is set correctly at tween endpoints.

### Manual Verification (Godot editor)

These are visual/interactive behaviours that require the Godot runtime:

- **Startup matrix:** Load fleet with 12+ hosts. Verify matrix covers grid, hosts appear individually with bars already at meaningful heights, matrix dissolves when all hosts ready.
- **Early selection:** Click a host before all hosts have loaded. Verify focus mode works, matrix dissolves, remaining hosts continue to appear.
- **Preview handoff:** Select a host, wait for preview to load. Verify matrix dissolves, beam dims, preview is visually prominent.
- **Preview animation:** After preview loads, verify bars are moving/breathing with metric updates, not static.
- **ESC flow:** From preview, press ESC. Verify beam fades to 0, camera returns to patrol cleanly. No errors from freed matrix.
- **Dive-in flow:** From animated preview, press Enter. Verify HostView loads with continuous animation, no flicker or reset.

## Architecture Notes

- The existing `MatrixProgressGrid` shader and script are reused without modification for the startup overlay — only the size and progress source differ.
- The holographic beam's `global_alpha` uniform already supports arbitrary values; we're just using a lower target.
- The animation fix is a single signal connection — no new components, no architectural changes.
