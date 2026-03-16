# Tasks: CPU Stacked Bar

**Input**: Design documents from `/specs/004-cpu-stacked-bar/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Included — project constitution mandates TDD (Red-Green-Refactor).

**Organization**: Tasks grouped by user story. US2 (smooth animation) requires zero code — existing infrastructure handles it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No new project setup needed — all infrastructure exists. This phase is a no-op.

**Checkpoint**: Ready to proceed directly to foundational work.

---

## Phase 2: Foundational (Ground Extent Fix)

**Purpose**: Fix `ComputeGroundExtent` to count visual columns (unstacked metrics + stack groups) instead of raw metric count. This MUST be done before user story work because stacked zones will produce incorrect ground bezels without it.

### Tests

- [x] T001 Write test `Calculate_StackedForegroundZone_GroundWidthReflectsVisualColumns` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` — create a 3-metric zone with 1 stack group, assert ground width matches 1-column extent not 3-column
- [x] T002 [P] Write test `Calculate_StackedBackgroundZone_GroundWidthReflectsVisualColumns` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` — create a per-instance zone with 3 metrics and 1 stack group, assert ground width matches 1-column not 3-column

### Implementation

- [x] T003 Fix `ComputeGroundExtent` foreground branch in `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` — compute visual column count as (ungrouped metrics + stack group count) instead of raw `zone.Metrics.Count`
- [x] T004 Fix `ComputeGroundExtent` background branch in `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` — same visual column count logic for background zones

**Checkpoint**: Ground extent tests pass for both foreground and background stacked zones. Existing tests still green.

---

## Phase 3: User Story 1 — Stacked CPU Bar Replaces Separate Bars (Priority: P1) 🎯 MVP

**Goal**: Aggregate CPU zone renders as 1 stacked bar (Sys red bottom, User green middle, Nice cyan top) instead of 3 separate orange bars.

**Independent Test**: Generate a layout from LinuxProfile and verify the CPU zone contains 1 PlacedStack with 3 correctly-coloured members in Sys/User/Nice order.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T005 [P] [US1] Write test `CpuZone_HasOneStackGroup_WithThreeMembers` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs` — assert StackGroups is non-null, has 1 group with 3 MetricLabels
- [x] T006 [P] [US1] Write test `CpuZone_StackGroup_OrderIsSysUserNice_ModeIsProportional` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs` — assert MetricLabels == ["Sys", "User", "Nice"] and Mode == Proportional
- [x] T007 [P] [US1] Write test `CpuZone_MetricColours_SysIsRed_UserIsGreen_NiceIsCyan` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs` — assert each metric's DefaultColour matches expected RGB
- [x] T008 [P] [US1] Write test `Calculate_CpuZone_HasOneStack_WithThreeMembers` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` — assert CPU zone items contain 1 PlacedStack (not 3 PlacedShapes), stack has 3 members
- [x] T009 [P] [US1] Write test `Calculate_CpuZone_StackMembers_HaveCorrectColours` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` — assert stack member colours are Red, Green, Cyan (in member order)

### Implementation for User Story 1

- [x] T010 [US1] Add `Cyan` colour constant to `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs` — `RgbColour.FromHex("#22d3ee")`
- [x] T011 [US1] Update `CpuZone()` in `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs` — reorder metrics to Sys/User/Nice, change colours to Red/Green/Cyan, add StackGroups with Proportional mode
- [x] T012 [US1] Update test `CpuZone_HasNoStackGroups` → rename to `CpuZone_HasStackGroups` and reverse assertion in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs`
- [x] T013 [US1] Update test `Calculate_CpuZone_HasThreeShapes_NoStacks` → assert 0 standalone shapes and 1 PlacedStack in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`
- [x] T014 [US1] Update test `Calculate_CpuZone_GroundWidthIsNominalFromMetricCount` → assert width reflects 1 visual column in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`
- [x] T015 [US1] Update test `Calculate_CpuZone_HasMetricLabels` if metric order changed in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`

**Checkpoint**: All LinuxProfile and LayoutCalculator tests pass. CPU zone produces 1 stacked bar with correct colours and ordering. Existing TscnWriter stack emission tests already validate .tscn output.

---

## Phase 4: User Story 2 — Stacked Bar Animates Smoothly (Priority: P2)

**Goal**: Segments animate smoothly when metric values change, maintaining stack integrity.

**Independent Test**: No new code or tests needed — this is already handled by existing infrastructure.

**Rationale**: `SceneBinder` applies smooth interpolation (exponential decay, SmoothSpeed=5.0) to each bar's `height` property individually. `StackGroupNode._process()` runs every frame and repositions children based on current `scale.y`. This combination already produces smooth stacked animation. Verified by existing test coverage in `TscnWriterTests` (stack emission) and `StackGroupNode.gd` runtime behaviour.

**Checkpoint**: Covered by US1 implementation + existing infrastructure. Visual verification in Godot confirms smooth animation.

---

## Phase 5: User Story 3 — Per-CPU Background Zone Also Stacked (Priority: P3)

**Goal**: Per-CPU background zone uses stacked bars (1 per CPU instance) with matching Sys/User/Nice colours.

**Independent Test**: Generate a layout with 4 CPUs and verify the Per-CPU zone contains 4 PlacedStack items (1 per CPU) instead of 12 PlacedShapes.

### Tests for User Story 3

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T016 [P] [US3] Write test `PerCpuZone_HasOneStackGroup_MatchingAggregateZone` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs` — assert StackGroups matches CPU zone config
- [x] T017 [P] [US3] Write test `PerCpuZone_MetricColours_MatchAggregateZone` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs` — assert Sys Red, User Green, Nice Cyan
- [x] T018 [P] [US3] Write test `Calculate_PerCpuZone_WithStacking_EmitsOneStackPerInstance` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` — with 4 CPUs, assert 4 PlacedStack items (not 12 shapes)
- [x] T019 [P] [US3] Write test `Calculate_PerCpuZone_StackMembers_HaveInstanceNames` in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` — assert each stack's members carry the correct instance name (e.g., "cpu0")

### Implementation for User Story 3

- [x] T020 [US3] Update `PerCpuZone()` in `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs` — reorder metrics to Sys/User/Nice, change colours to Red/Green/Cyan, add matching StackGroups
- [x] T021 [US3] Extend `BuildBackgroundShapes()` in `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` — check `StackedMetricLabels(zone)`, group metrics per instance into PlacedStack items, emit one stack per instance for grouped metrics
- [x] T022 [US3] Update test `Calculate_PerCpuZone_ShapeCountEqualsInstancesTimesMetrics` → adjust assertion for stacks in `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`
- [x] T023 [US3] Update `MetricLabels` assignment in `PlaceZone()` in `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` — for zones with stacking, metric labels should reflect visual columns (stack group names for stacked metrics, individual labels for unstacked)

**Checkpoint**: Per-CPU zone produces N stacked bars with correct colours. All existing background layout tests updated and passing.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T024 Run full test suite `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"` and verify all tests green
- [x] T025 Visual verification — generate a scene using quickstart.md steps and confirm in Godot: 1 aggregate stacked bar + N per-CPU stacked bars with correct colours

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No-op — skip
- **Phase 2 (Foundational)**: No dependencies — start immediately. BLOCKS user stories.
- **Phase 3 (US1)**: Depends on Phase 2 completion
- **Phase 4 (US2)**: No work needed — covered by existing infrastructure
- **Phase 5 (US3)**: Depends on Phase 2 completion. Can run in parallel with US1 (different zones, different code paths). However, sequential execution is recommended (US1 first) so US3 can follow the same pattern.
- **Phase 6 (Polish)**: Depends on US1 and US3 completion

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Profile changes (LinuxProfile.cs) before layout changes (LayoutCalculator.cs)
- New tests before updating existing tests
- Run test suite after each implementation task

### Parallel Opportunities

**Phase 2**: T001 and T002 can run in parallel (different test methods, no shared state)

**Phase 3 (US1)**: T005–T009 can ALL run in parallel (all are new test methods in different test classes)

**Phase 5 (US3)**: T016–T019 can ALL run in parallel (all are new test methods)

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests in parallel (they all write to different test methods):
Task: "Write test CpuZone_HasOneStackGroup_WithThreeMembers in LinuxProfileTests.cs"
Task: "Write test CpuZone_StackGroup_OrderIsSysUserNice_ModeIsProportional in LinuxProfileTests.cs"
Task: "Write test CpuZone_MetricColours_SysIsRed_UserIsGreen_NiceIsCyan in LinuxProfileTests.cs"
Task: "Write test Calculate_CpuZone_HasOneStack_WithThreeMembers in LayoutCalculatorTests.cs"
Task: "Write test Calculate_CpuZone_StackMembers_HaveCorrectColours in LayoutCalculatorTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 2: Foundational (T001–T004) — ground extent fix
2. Complete Phase 3: User Story 1 (T005–T015) — aggregate CPU stacked bar
3. **STOP and VALIDATE**: Run tests, verify in Godot
4. This alone delivers the core visual change

### Incremental Delivery

1. Phase 2 → Foundation ready
2. Phase 3 (US1) → Aggregate CPU stacked bar → Test → Demo (MVP!)
3. Phase 4 (US2) → No work needed — animation verified
4. Phase 5 (US3) → Per-CPU stacked bars → Test → Demo
5. Phase 6 → Full verification pass

---

## Notes

- Total tasks: **25**
- US1 tasks: **11** (5 tests + 6 implementation/updates)
- US2 tasks: **0** (existing infrastructure)
- US3 tasks: **8** (4 tests + 4 implementation/updates)
- Foundational tasks: **4** (2 tests + 2 implementation)
- Polish tasks: **2**
- Parallel opportunities: **14 tasks** marked [P]
- Suggested MVP scope: US1 only (Phase 2 + Phase 3)
- All tasks follow checklist format with checkbox, ID, labels, and file paths
