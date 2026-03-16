# macOS First-Class Citizen

**Date**: 2026-03-16
**Issue**: #4 (expanded scope beyond export presets)
**Related**: PCP upstream [performancecopilot/pcp#2532](https://github.com/performancecopilot/pcp/issues/2532), pmview-nextgen [#35](https://github.com/tallpsmith/pmview-nextgen/issues/35)

## Problem

The current `MacOsProfile` delegates entirely to `LinuxProfile`, but the PCP Darwin PMDA has significant metric namespace gaps. Connecting to a macOS pmproxy with the Linux profile would produce broken bindings for missing metrics. macOS deserves a profile that reflects its actual metric landscape.

## Research Findings

### Metric Parity Audit

| Category | Linux Metrics | Darwin Status |
|----------|--------------|---------------|
| CPU aggregate | `kernel.all.cpu.{user,sys,nice}` | Identical |
| CPU per-instance | `kernel.percpu.cpu.{user,sys,nice}` | Identical |
| Load | `kernel.all.load` (1/5/15 min) | Identical |
| Memory | `mem.util.{used,cached,bufmem}` | `used` only. No `cached` or `bufmem` |
| Disk aggregate | `disk.all.{read,write}_bytes` | Identical |
| Disk per-instance | `disk.dev.{read,write}_bytes` | Identical |
| Network aggregate | `network.all.{in,out}.{bytes,packets}` | **Entirely absent** |
| Network per-interface | `network.interface.{in,out}.{bytes,packets,errors}` | Identical |

### macOS Memory Metrics Available

Darwin provides a different but richer memory model:

- `mem.util.used` — wired + active + inactive (different composition than Linux)
- `mem.util.wired` — non-pageable kernel memory
- `mem.util.active` — recently referenced pages
- `mem.util.inactive` — pages eligible for reclaim (closest analogue to Linux "cached")
- `mem.util.compressed` — macOS memory compressor (significant on modern Macs)
- `mem.util.free` — truly free pages

## Design

### 1. CPU, Load, Disk Zones — No Changes

Metrics are identical on both platforms. `MacOsProfile` reuses the same zone definitions as Linux for these categories.

### 2. Memory Zone — macOS-Specific Expanded View

Instead of forcing Linux's `used/cached/bufmem` model, the macOS Memory zone shows 4 metrics that tell the real macOS memory story:

| Metric | Label | Colour | Rationale |
|--------|-------|--------|-----------|
| `mem.util.wired` | Wired | Red | Non-pageable, kernel-held — most constrained |
| `mem.util.active` | Active | Green | Recently used — healthy working set |
| `mem.util.inactive` | Inactive | Amber | Reclaimable — available if needed |
| `mem.util.compressed` | Compressed | Blue | macOS-specific, significant on modern Macs |

Source range max is resolved via `HostTopology.PhysicalMemoryBytes`, which is populated at topology-discovery time. The existing `LayoutCalculator.ResolveSourceRangeMax` method already handles any metric prefixed with `mem.` by substituting this value — no new code needed for range resolution.

Whether to display these as 4 separate bars or a stacked bar (like the CPU zone) is a visual decision to be made during implementation — both options are viable.

### 3. Network Aggregate Zones — Ghost Placeholders

The `network.all.*` namespace does not exist on Darwin. Rather than omitting the Net-In and Net-Out foreground zones (which would create an asymmetric layout), they are rendered as **ghost placeholders**. Each ghost zone contains 2 shapes (Bytes + Packets), matching the Linux layout, both marked as placeholders.

An upstream PCP issue has been filed ([performancecopilot/pcp#2532](https://github.com/performancecopilot/pcp/issues/2532)) to add `network.all.*` to the Darwin PMDA. Once that lands, the ghost flag is simply removed from these metrics.

### 4. Ghost Shape Mechanism

A general-purpose mechanism for rendering placeholder shapes when a metric is known to be unavailable.

**Declaration**: `IsPlaceholder` boolean flag on `MetricShapeMapping` (default `false`). Must also propagate through `PlacedShape` — the `LayoutCalculator.BuildShape` method threads the flag from the definition model into the layout model, since `TscnWriter` only reads `PlacedShape`, never `MetricShapeMapping` directly.

**Rendering behaviour for placeholder shapes:**
- **Height**: Locked at max target range (e.g., 5.0 units) — shows the full extent of what the bar *could* represent
- **Colour**: Desaturated to grey (e.g., `Color(0.5, 0.5, 0.5)`)
- **Opacity**: ~20-30% alpha — requires both setting the alpha channel on `albedo_color` AND enabling `TRANSPARENCY_ALPHA` mode on the `StandardMaterial3D` (Godot ignores alpha without the transparency mode flag)
- **Binding**: No `PcpBindingResource` emitted. No metric polling. The shape is purely visual.
- **Animation**: None — static at max height

**Granularity**: Per-shape, not per-zone. A zone can mix live and ghost metrics. If all metrics in a zone happen to be placeholders, the zone structure (bezel, title, grid) renders normally — only the shapes inside are ghosted.

**Implementation layers affected:**

| Layer | File(s) | Change |
|-------|---------|--------|
| Definition model | `ZoneDefinition.cs` (`MetricShapeMapping`) | Add `IsPlaceholder` flag (default `false`) |
| Layout model | `SceneLayout.cs` (`PlacedShape`) | Add `IsPlaceholder` flag |
| Layout calculator | `LayoutCalculator.cs` (`BuildShape`) | Thread `IsPlaceholder` from mapping to placed shape |
| Scene emission | `TscnWriter.cs` (`CollectSubResources` + `WriteShape`) | Skip `PcpBindingResource` sub-resource registration AND `PcpBindable` child node emission for placeholders. Both sites must be guarded to avoid orphaned sub-resources and inflated `load_steps`. Emit grey colour + ghost property instead. |
| Building block | `grounded_shape.gd` | Add `ghost: bool` export property. When true: override albedo to grey, enable `TRANSPARENCY_ALPHA` on material, set alpha to ~0.25. Applied in `_ready()` or when property changes. |
| Scene binder | `SceneBinder.cs` | No changes needed — `SceneBinder` discovers bindings by walking `PcpBindable` child nodes. Ghost shapes have no `PcpBindable` child, so they're naturally skipped. |

**Future**: Runtime auto-ghosting (detect unavailable metrics at poll time) captured as issue [#35](https://github.com/tallpsmith/pmview-nextgen/issues/35).

### 5. Network Per-Interface — No Changes

`network.interface.*` metrics are identical on both platforms.

## Profile Structure

```
MacOsProfile.GetZones() returns:
  - CpuZone()          → shared (stacked, Red/Green/Cyan)
  - LoadZone()         → shared
  - MemoryZone()       → macOS-specific: Wired/Active/Inactive/Compressed
  - DiskTotalsZone()   → shared
  - NetInAggregate()   → ghost placeholder (both Bytes + Pkts shapes IsPlaceholder=true)
  - NetOutAggregate()  → ghost placeholder (both Bytes + Pkts shapes IsPlaceholder=true)
  - PerCpuZone()       → shared (stacked, Red/Green/Cyan)
  - PerDiskZone()      → shared
  - NetworkInZone()    → shared
  - NetworkOutZone()   → shared
```

**Zone reuse approach**: Extract the 7 shared zone methods from `LinuxProfile` into an `internal static class SharedZones` (or similar). `LinuxProfile` and `MacOsProfile` both call into it. This is necessary because `LinuxProfile`'s zone methods are currently `private static` — `MacOsProfile` cannot access them directly. The colour constants (Red, Green, Cyan, Indigo, Amber, etc.) should also move to the shared helper since `MacOsProfile`'s memory zone needs access to the palette.

## Out of Scope

- Godot export presets / bundling (original Issue #4 scope — separate work)
- macOS code signing / notarisation
- Client-side metric aggregation for `network.all.*`
- Runtime auto-ghosting (captured in #35)
- macOS-specific bonus metrics (APFS, thermal, battery, GPU) — future enrichment
