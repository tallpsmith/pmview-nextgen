# Scene Layout Polish — Design Spec

**Date:** 2026-03-14
**Status:** Approved

## Overview

A set of coordinated visual improvements to the generated host-view scene:
zone reordering, a merged System zone, network aggregate zones, tighter spacing,
larger fonts, a neon timestamp burned into the floor, and a floating film-title
hostname above the scene.

---

## 1. Zone Layout Reorganisation

### Foreground row (aggregate bars/cylinders)

| Position | Zone | Contents |
|----------|------|----------|
| 1 | **System** | CPU (User/Sys/Nice) + Load (1m/5m/15m) + Memory (Used/Cached/Buf) |
| 2 | **Disk** | disk.all.read_bytes / disk.all.write_bytes |
| 3 | **Net-In** | network.all.in.bytes / network.all.in.packets |
| 4 | **Net-Out** | network.all.out.bytes / network.all.out.packets |

### Background row (per-instance grids)

| Position | Zone | Contents |
|----------|------|----------|
| 1 | **Per-CPU** | kernel.percpu.cpu.user/sys/nice per core |
| 2 | **Per-Disk** | disk.dev.read_bytes/write_bytes per device |
| 3 | **Net-In** | network.interface.in.bytes/packets/errors per interface |
| 4 | **Net-Out** | network.interface.out.bytes/packets/errors per interface |

Columns align front-to-back: System over Per-CPU, Disk over Per-Disk, etc.

### Network aggregate metric names

Use `network.all.in.bytes`, `network.all.in.packets`, `network.all.out.bytes`,
`network.all.out.packets`. If pmproxy does not expose these, the zone placeholders
remain but bars idle at zero — acceptable for now.

---

## 2. System Zone (merged CPU + Load + Memory)

`LinuxProfile` replaces the three separate `CpuZone`, `LoadZone`, `MemoryZone`
definitions with a single `SystemZone` containing all 9 metrics. One `ZoneDefinition`,
one ground bezel, one zone label ("System").

The zone will be noticeably wider than its neighbours (~9 × ShapeSpacing), which
is intentional — it is the centrepiece aggregate group.

---

## 3. Spacing Adjustments

| Constant | Current | Proposed |
|----------|---------|----------|
| `ZoneGap` | 3.0f | 2.0f |
| `ShapeSpacing` | 1.5f | 1.2f |

Reduces dead air between zones and between individual bars without crowding labels.
Values are in `LayoutCalculator`.

---

## 4. Font Size Increases

All Label3D nodes in the generated scene are readable up close today but need
to be legible at a distance (wall display, presentation).

| Label type | Current font_size | Proposed font_size | pixel_size |
|------------|------------------|-------------------|------------|
| Zone label | 32 | 56 | 0.01 |
| Shape label (foreground) | 24 | 40 | 0.008 → 0.01 |
| Grid column header | 24 | 40 | 0.008 → 0.01 |
| Grid row header | 24 | 40 | 0.008 → 0.01 |

All values are in `TscnWriter`.

---

## 5. Text Binding — Virtual Metrics for Scene Metadata

The existing binding system is numeric-only (`TargetProperty = "height"` etc.).
We extend it to support string-valued bindings so that any Label3D in any generated
scene can display live scene metadata using the same pipeline as numeric metrics.

**Motivation:** Multiple text properties benefit from this pattern — archive timestamp,
hostname, archive name, pmproxy URL, and any future metadata. Making these
first-class bindings keeps `host_view_controller.gd` dumb and avoids one-off signal
wiring per display.

### New virtual metric names (injected by MetricPoller, not fetched from pmproxy)

| Virtual metric | Content |
|----------------|---------|
| `pmview.meta.timestamp` | Current archive playback time — `YYYY-MM-DD · HH:MM:SS` |
| `pmview.meta.hostname` | Hostname from scene topology |
| `pmview.meta.archive` | Archive name / path (from pmproxy context) |
| `pmview.meta.endpoint` | pmproxy endpoint URL |

### Binding system extensions

- `PcpBindingResource` gains a `StringValue` variant path: when `TargetProperty = "text"`,
  the binder sets the node's `text` property instead of a float.
- `SceneBinder.cs` handles `TargetProperty == "text"` by calling `Set("text", value)`
  on the bound node.
- `MetricPoller.cs` populates the virtual metrics from its own state on each poll cycle
  and includes them in the metric value map alongside real PCP metrics.

### Timestamp Floor Label

A large Label3D laid flat on the floor between the two rows, bound to
`pmview.meta.timestamp`.

**Appearance**
- `font_size = 96`, `pixel_size = 0.02`
- Colour: amber/orange (`#f97316`) matching the CPU/System palette
- `outline_size = 8`, outline colour black
- Transform: rotated 90° around X (lies flat), centered on X=0, Y=0.02 (just above bezel), Z=-4 (between rows)
- `horizontal_alignment = 1` (centred)

`TscnWriter` emits a `TimestampLabel` node (Label3D) with a `PcpBindable` child
wired to `pmview.meta.timestamp` / `TargetProperty = "text"`, as a direct child
of `HostView`.

---

## 6. Hostname Title Label

A large floating Label3D centered high above the scene, always facing the camera.

**Appearance**
- `font_size = 128`, `pixel_size = 0.015`
- Colour: white (`#ffffff`)
- `outline_size = 12`, outline colour black (`#000000`)
- `billboard = 1` (Godot `BaseMaterial3D.BILLBOARD_ENABLED`)
- Position: X=0, Y=10, Z=0 (above scene centre)
- `horizontal_alignment = 1` (centred)
- `uppercase = true`

**Content:** bound to `pmview.meta.hostname` virtual metric — same pipeline as the
timestamp label.

`TscnWriter` emits a `HostnameLabel` node (Label3D) with a `PcpBindable` child
wired to `pmview.meta.hostname` / `TargetProperty = "text"`, as a direct child
of `HostView`.

---

## Architecture Impact

| Layer | Change |
|-------|--------|
| `LinuxProfile.cs` | Replace CPU/Load/Memory zones with merged System zone; add Net-In/Out aggregate zones |
| `MacOsProfile.cs` | Same System zone merge; add Net-In/Out aggregate zones (parity with Linux) |
| `LayoutCalculator.cs` | Adjust `ZoneGap` and `ShapeSpacing` constants |
| `TscnWriter.cs` | Larger font sizes; emit `TimestampLabel` and `HostnameLabel` nodes bound via text binding |
| `PcpBindingResource.cs` | Add string-value binding path; support `TargetProperty = "text"` |
| `SceneBinder.cs` | Handle `TargetProperty == "text"` — call `Set("text", value)` on bound node |
| `MetricPoller.cs` | Inject virtual `pmview.meta.*` metrics on each poll cycle |
| `host_view_controller.gd` | No special timestamp wiring needed — binding system handles it |

Tests are required for all C# changes (`LayoutCalculatorTests`, `SceneEmitterTests`,
`TscnWriterTests`, `LinuxProfileTests`). The signal and GDScript wiring is tested
manually in Godot.

---

## Out of Scope

- Playback controls (pause/scrub) — timestamp is display-only for now
- Colour theming for the System zone sub-groups (all bars same orange for now)
- MacOS-specific network aggregate metric names (use same names, accept zero if absent)
