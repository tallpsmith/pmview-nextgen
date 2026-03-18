# Range Tuning Panel Design

**Date:** 2026-03-17
**Status:** Approved

## Problem

Source range values (SourceRangeMax) for disk and network metrics are hardcoded in SharedZones at profile definition time. Real environments vary wildly — a 100 Gbit NIC vs a 1 Gbit NIC, NVMe Gen5 vs spinning rust. Users need runtime control to calibrate the 3D visualization to their hardware, preventing shapes from either clipping at the ceiling or being invisibly small.

## Scope

- **Tuneable zones:** Disk (aggregate), Per-Disk, Network (In & Out share one slider)
- **Tuneable property:** SourceRangeMax only — target range stays under profile author control
- **Tuneable metrics within a zone:** Only bytes-throughput metrics (metric names containing `bytes`). Packet counts and error counts retain their profile defaults and are not affected by the slider.
- **Out of scope:** CPU and Load (universal 0-100% / 0-10), persistence of tuned values across sessions

## Architecture: Approach C — GDScript UI Panel → SceneBinder Method Call

GDScript owns the UI panel. On Apply, it calls a public method on SceneBinder (C#) with the zone name and new max. SceneBinder updates its internal bindings. The next MetricPoller tick picks up the new ranges through the existing `Normalise()` path.

### Data Flow

```
User drags slider, hits Apply
    ↓
range_tuning_panel.gd → scene_binder.UpdateSourceRangeMax("Per-Disk", 7_000_000_000.0)
    ↓
SceneBinder replaces MetricBinding records (with expression) for matching zone
    ↓
Next MetricPoller tick → ApplyMetrics() → Normalise() uses new SourceRangeMax
    ↓
3D shapes rescale
```

### Zone Identity: Baked at Generation Time

Zone names flow from SharedZones through the generation pipeline into the scene:

1. **SharedZones** (projection-core) defines zone names and metrics. New static resolver methods:
   - `ResolveZone(string metricName) → string?` — returns zone name for a metric
   - `GetMetricNames(string zoneName) → string[]` — returns all metrics in a zone

2. **PlacedZone** (projection-core) already carries the zone `Name`. `TscnWriter` and `RuntimeSceneBuilder` read the zone name from the parent `PlacedZone` context when emitting each binding's `ZoneName` property — no new field needed on `PlacedShape`.

3. **TscnWriter / RuntimeSceneBuilder** emits `ZoneName` on each `PcpBindingResource`.

4. **PcpBindingResource** (addon) gets `[Export] public string ZoneName`.

5. **MetricBinding** (PcpGodotBridge) gets an optional `string? ZoneName` field.

6. **SceneBinder** groups bindings by `ZoneName` for range updates.

The addon never calls SharedZones directly — it reads zone identity from the scene. Clean layer separation.

### SceneBinder API Changes

```csharp
// Emitted at the end of BindFromSceneProperties() once all bindings are
// discovered and validated. Panel connects in _ready() and queries on signal.
[Signal]
public delegate void BindingsReadyEventHandler();

// True after BindFromSceneProperties() completes. Late-loading panels
// check this and call GetSourceRanges() directly instead of waiting for signal.
public bool IsBound { get; private set; }

// Updates SourceRangeMax for bytes-throughput bindings in the named zone.
// Only affects bindings whose metric name contains "bytes".
// Takes effect on next poll tick (lazy re-normalisation).
// Thread-safe: both this and ApplyMetrics() run on the Godot main thread.
public void UpdateSourceRangeMax(string zoneName, double newMax)

// Returns {zoneName: currentSourceRangeMax} for each zone found in active bindings.
// Returns only the bytes-throughput binding's SourceRangeMax per zone.
// Zones with no active bindings are omitted — callers must guard for missing keys.
public Godot.Collections.Dictionary GetSourceRanges()
```

### Binding Mutation via `with` Expressions

MetricBinding, ResolvedBinding, and ActiveBinding are all immutable records. Mutation uses C# `with` expressions to rebuild the chain:

```csharp
for (int i = 0; i < _activeBindings.Count; i++)
{
    var active = _activeBindings[i];
    if (active.Resolved.Binding.ZoneName != zoneName) continue;
    if (!active.Resolved.Binding.Metric.Contains("bytes")) continue;

    var oldBinding = active.Resolved.Binding;
    var newBinding = oldBinding with { SourceRangeMax = newMax };
    var newResolved = active.Resolved with { Binding = newBinding };
    var newActive = active with { Resolved = newResolved };

    // Migrate smooth interpolation state to new key
    if (_smoothValues.TryGetValue(active, out var smoothState))
    {
        _smoothValues.Remove(active);
        _smoothValues[newActive] = smoothState;
    }

    _activeBindings[i] = newActive;
}
```

**Implementation notes:**
- Replace by index — records use value equality so `Remove()` would match the wrong entry after mutation.
- Smooth interpolation state (`_smoothValues`) is migrated to the new `ActiveBinding` key to avoid a visual snap on update.
- Only bytes-throughput bindings are affected; packet/error bindings in the same zone are left untouched.

## UI Panel Design

### Layout

Floating, draggable `PanelContainer` overlay with rounded corners and semi-transparent/translucent background (`StyleBoxFlat` with `corner_radius` and alpha on `bg_color`). Toggled via toolbar button or hotkey. Does not block the 3D view.

### Sliders

Three log-scaled `HSlider` controls, colour-coded to match zone colours from SharedZones:

| Slider | Zone | Colour | Default (from profile) |
|--------|------|--------|---------|
| Disk (Total) | Disk | Amber | 500 MB/s |
| Per-Disk | Per-Disk | Green | ~488 KB/s |
| Network | Network In + Out | Blue | 125 MB/s |

Defaults shown are illustrative — the panel always initialises from `GetSourceRanges()` which returns the actual values from active bindings.

### Preset Notches

Tick marks at common hardware speeds with snap-to behaviour near thresholds:

**Disk presets:** HDD (150 MB/s), SATA SSD (550 MB/s), NVMe Gen3 (3.5 GB/s), NVMe Gen4 (7 GB/s), NVMe Gen5 (14 GB/s)

**Network presets:** 1 Gbit (125 MB/s), 10 Gbit (1.25 GB/s), 25 Gbit (3.125 GB/s), 40 Gbit (5 GB/s), 100 Gbit (12.5 GB/s)

### Behaviour

- **Human-readable readout** — current value displayed beside each slider, auto-scaling units (KB/s → MB/s → GB/s)
- **Log scale** — slider is logarithmically scaled to cover MB/s through tens of GB/s usably
- **Apply button** — calls `UpdateSourceRangeMax()` for each zone that changed. Enabled only when values differ from current.
- **Initialisation** — panel connects to `BindingsReady` signal, then calls `GetSourceRanges()` to populate sliders with current values. If the panel loads after bindings are already resolved (late instantiation), it checks `SceneBinder.IsBound` and queries immediately as a fallback.
- **Log scale implementation** — Godot's `HSlider` is linear. The panel applies `log()` / `exp()` transforms between slider position (0.0–1.0) and the actual byte/s value. Preset notch positions are pre-calculated at these log-space coordinates.
- **Missing zones** — if `GetSourceRanges()` omits a zone (e.g. host has no disks), the corresponding slider is hidden rather than showing a stale default.

### GDScript Usage

```gdscript
# On bindings_ready signal:
var ranges = scene_binder.GetSourceRanges()
disk_total_slider.value = ranges["Disk"]
per_disk_slider.value = ranges["Per-Disk"]
network_slider.value = ranges["Network In"]

# On Apply button:
scene_binder.UpdateSourceRangeMax("Disk", disk_total_slider.value)
scene_binder.UpdateSourceRangeMax("Per-Disk", per_disk_slider.value)
scene_binder.UpdateSourceRangeMax("Network In", network_slider.value)
scene_binder.UpdateSourceRangeMax("Network Out", network_slider.value)
```

## Changes By Project

| Project | Changes |
|---------|---------|
| **pmview-projection-core** | `SharedZones`: add `ResolveZone()`, `GetMetricNames()` (internal visibility, use `[InternalsVisibleTo]` for test projects). |
| **pmview-host-projector** | `TscnWriter`: emit `ZoneName` on each `PcpBindingResource`. |
| **PcpGodotBridge** | `MetricBinding`: add optional `ZoneName` field. `PcpBindingConverter`: pass through zone name. |
| **pmview-bridge-addon** | `PcpBindingResource`: add `[Export] ZoneName`. `SceneBinder`: add `BindingsReady` signal, `UpdateSourceRangeMax()`, `GetSourceRanges()`. New `range_tuning_panel.gd`. |
| **pmview-app** | `RuntimeSceneBuilder`: set `ZoneName` when creating bindings. |
