# Implementation Plan: CPU Stacked Bar

**Branch**: `004-cpu-stacked-bar` | **Date**: 2026-03-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-cpu-stacked-bar/spec.md`

## Summary

Replace the 3 separate CPU bars with a single stacked bar showing Sys (red, bottom), User (green, middle), Nice (cyan, top). The stacking infrastructure already exists in the codebase — `MetricStackGroupDefinition`, `StackGroupNode.gd`, `PlacedStack`, and `TscnWriter.WriteStack()` are all implemented and tested. This feature is primarily a **profile configuration change** with some additional work to support stacking in background (per-instance) zones.

## Technical Context

**Language/Version**: C# (.NET 8.0 for libraries, .NET 10.0 for CLI/tests); GDScript for building blocks
**Primary Dependencies**: Godot 4.6+, System.Text.Json, Tomlyn
**Storage**: N/A (scene generation, no persistence)
**Testing**: xUnit (C# tests), gdUnit4 (GDScript — not applicable here)
**Target Platform**: Cross-platform (Linux primary, macOS dev)
**Project Type**: Godot addon + CLI scene generator
**Performance Goals**: 60 FPS rendering; stacking adds no measurable overhead (already runs per-frame in `StackGroupNode._process()`)
**Constraints**: No new dependencies; must integrate with existing binding pipeline
**Scale/Scope**: 2 zone definitions changed, 1 method extended

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Prototype-First | **PASS** | Not needed — stacking infrastructure is already built and tested. This is a configuration change using proven building blocks. No new capability being explored. |
| II. TDD (NON-NEGOTIABLE) | **PASS** | Tests will be written first for: (1) updated LinuxProfile colours/stack groups, (2) LayoutCalculator background stacking, (3) TscnWriter emission with new colours. Existing tests for stack infrastructure already pass. |
| III. Code Quality | **PASS** | Minimal code changes — reusing existing abstractions. No new abstractions introduced. |
| IV. UX Consistency | **PASS** | Stacked bar uses the same visual vocabulary (height = utilisation). Colours are user-specified. Deviates from "all orange" but this is intentional — the stacked bar *requires* per-segment colours for readability. |
| V. Performance | **PASS** | `StackGroupNode._process()` already runs for any stacked zone. Adding it to CPU zones is identical overhead to existing stacking support. |

## Project Structure

### Documentation (this feature)

```text
specs/004-cpu-stacked-bar/
├── plan.md              # This file
├── research.md          # Phase 0 output (complete)
├── data-model.md        # Phase 1 output (complete)
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (files to modify)

```text
src/pmview-host-projector/
├── src/PmviewHostProjector/
│   ├── Profiles/LinuxProfile.cs           # MODIFY: CPU colours + StackGroups
│   └── Layout/LayoutCalculator.cs         # MODIFY: BuildBackgroundShapes stack support
└── tests/PmviewHostProjector.Tests/
    ├── Profiles/LinuxProfileTests.cs      # MODIFY: Update tests for stack groups + colours
    └── Layout/LayoutCalculatorTests.cs    # MODIFY: Add background stacking tests, update CPU count tests
```

**Structure Decision**: No new files or directories. All changes are modifications to existing files in the existing project structure.

## Implementation Approach

### Phase 1: Aggregate CPU Zone (P1 — core feature)

**What changes**: `LinuxProfile.CpuZone()` gets per-metric colours and a `StackGroups` parameter.

**LinuxProfile.cs changes**:
- Add `Cyan` colour constant: `RgbColour.FromHex("#22d3ee")` (Tailwind cyan-400)
- Change CPU metric colours: Sys → `Red`, User → `Green`, Nice → `Cyan`
- Add `StackGroups` parameter to `CpuZone()`:
  ```
  StackGroups: [new MetricStackGroupDefinition("CPU", StackMode.Proportional, ["Sys", "User", "Nice"])]
  ```
- Reorder metrics to match stack order (bottom-to-top): Sys first, then User, then Nice

**Tests to write/update (TDD — write first)**:
1. `CpuZone_HasStackGroup_WithThreeMembers` — verify StackGroups is non-null, has 1 group, 3 labels
2. `CpuZone_StackGroup_OrderIsSysUserNice` — verify MetricLabels order matches bottom-to-top
3. `CpuZone_StackGroup_ModeIsProportional` — verify mode
4. `CpuZone_MetricColours_SysIsRed_UserIsGreen_NiceIsCyan` — verify per-metric colours
5. Update `CpuZone_HasNoStackGroups` → reverse: assert StackGroups is NOT null
6. Update `Calculate_CpuZone_HasThreeShapes_NoStacks` → assert 0 standalone shapes, 1 PlacedStack with 3 members

**LayoutCalculator impact**: None for foreground — `BuildForegroundItems` already handles `StackGroups`. The existing test `Calculate_CpuZone_HasThreeShapes_NoStacks` will need updating because CPU will now have 0 direct shapes and 1 stack.

**TscnWriter impact**: None — `WriteStack()` already handles `PlacedStack` items. Existing stack emission tests already cover this path.

**Ground extent computation**: `ComputeGroundExtent` for foreground uses `zone.Metrics.Count` for nominal width. With stacking, 3 metrics become 1 visual column. This needs a fix:
- Currently: `(3-1)*1.2 + 0.8 + 1.2 = 4.4` (width for 3 bars)
- Should be: `(1-1)*1.2 + 0.8 + 1.2 = 2.0` (width for 1 stacked bar)
- Fix: count visual columns (unstacked metrics + stack groups) instead of raw metric count

**Test**: `Calculate_CpuZone_GroundWidthIsNominalFromMetricCount` → update expected width for 1-column stacked zone

### Phase 2: Smooth Animation (P2)

**No code changes needed**. The existing `SceneBinder` applies smooth interpolation to each bar's `height` property individually, and `StackGroupNode._process()` repositions children every frame based on current `scale.y`. This already produces smooth stacked animation. The acceptance scenarios in the spec are satisfied by the existing infrastructure.

### Phase 3: Per-CPU Background Zone (P3)

**What changes**: `LinuxProfile.PerCpuZone()` gets the same colours and StackGroups. `LayoutCalculator.BuildBackgroundShapes()` needs to support stack groups per instance.

**LinuxProfile.cs changes**:
- Same colour changes for `PerCpuZone()`: Sys → Red, User → Green, Nice → Cyan
- Add same `StackGroups` parameter

**LayoutCalculator.BuildBackgroundShapes changes**:
- Check for `StackedMetricLabels(zone)` (reuse existing method)
- Group metrics per instance into stacks where applicable
- Emit `PlacedStack` items per instance instead of individual shapes

**ComputeGroundExtent for background**:
- Currently: `cols = zone.Metrics.Count` → 3 columns per instance
- With stacking: should be 1 column per instance (the stack replaces 3 columns)
- Fix: count visual columns (unstacked + stack group count) instead of raw metrics

**MetricLabels for grid headers**:
- Currently: `["User", "Sys", "Nice"]` → 3 column headers
- With stacking: should be `["CPU"]` (the stack group name) — or just the single stack label
- The `MetricGrid` will arrange 1 column per instance row instead of 3

**Tests to write/update (TDD)**:
1. `PerCpuZone_HasStackGroup` — verify StackGroups matches aggregate
2. `Calculate_PerCpuZone_ShapeCountEqualsInstancesTimesMetrics` → update: now expect N stacks (1 per CPU) instead of N*3 shapes
3. `Calculate_PerCpuZone_WithStacking_EmitsOneStackPerInstance` — new test
4. `Calculate_PerCpuZone_StackMembers_HaveInstanceNames` — verify instance names propagate through stacks
5. Background ground extent tests — verify width shrinks from 3-column to 1-column

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Ground extent calculation wrong for stacked zones | Medium | Low | Straightforward formula fix; unit tested |
| MetricGrid column headers don't align with stacked layout | Low | Medium | MetricGrid already handles arbitrary column counts; test in Godot |
| Per-CPU stacking breaks MetricGrid row arrangement | Low | Medium | MetricGrid arranges children by order; stacks are single children per column |

## Complexity Tracking

No complexity violations. All changes use existing abstractions — no new patterns, dependencies, or layers introduced.
