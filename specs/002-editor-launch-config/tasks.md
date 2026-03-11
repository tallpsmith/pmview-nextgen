# Tasks: Editor Launch Configuration

**Input**: Design documents from `/specs/002-editor-launch-config/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/project-settings-schema.md

**Tests**: TDD required for TimeCursor.Loop (plan Phase B). EditorPlugin and scene integration verified manually in Godot editor.

**Organization**: Tasks grouped by user story. US0 is a structural prerequisite (foundational phase).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No project initialization needed — extending an existing codebase. This phase is intentionally empty.

**Checkpoint**: Ready to begin foundational restructure.

---

## Phase 2: Foundational — Addon Restructure (US0, Priority: P0)

**Goal**: Relocate bridge code from `scripts/bridge/` to `addons/pmview-bridge/`, create `plugin.cfg`, verify existing scenes still function.

**Independent Test**: Existing scenes (disk_io_panel, test_bars) load and function identically after the move — same metrics, same bindings, same behaviour.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. The EditorPlugin must live under `addons/<name>/`.

- [x] T001 Create addon directory `godot-project/addons/pmview-bridge/`
- [x] T002 Create `plugin.cfg` manifest in `godot-project/addons/pmview-bridge/plugin.cfg` (name: "pmview-bridge", script: PmviewBridgePlugin.cs, per R5 in research.md)
- [x] T003 Move `godot-project/scripts/bridge/MetricPoller.cs` to `godot-project/addons/pmview-bridge/MetricPoller.cs`
- [x] T004 [P] Move `godot-project/scripts/bridge/SceneBinder.cs` to `godot-project/addons/pmview-bridge/SceneBinder.cs`
- [x] T005 [P] Move `godot-project/scripts/bridge/MetricBrowser.cs` to `godot-project/addons/pmview-bridge/MetricBrowser.cs`
- [x] T006 Update C# namespace declarations in moved files if they reference `scripts.bridge` or similar path-based namespaces
- [x] T007 Update all `.tscn` `[ext_resource]` paths from `res://scripts/bridge/` to `res://addons/pmview-bridge/` in `godot-project/scenes/main.tscn` and any other `.tscn` files
- [x] T008 Update any GDScript `preload()` or string path references to bridge scripts in `godot-project/scripts/scenes/metric_scene_controller.gd`
- [x] T009 Update `godot-project/pmview-nextgen.csproj` if it has explicit file includes referencing old paths
- [x] T010 Delete `godot-project/scripts/bridge/` directory after confirming all files moved
- [x] T011 Create placeholder `godot-project/addons/pmview-bridge/PmviewBridgePlugin.cs` — empty `EditorPlugin` subclass with `[Tool]` attribute (skeleton only, implemented in US1)

**Checkpoint**: User loads project in Godot, enables plugin in Plugin Manager, runs existing scenes — everything works as before. `scripts/bridge/` no longer exists.

---

## Phase 3: User Story 1 — Configure Archive Playback Before Launch (Priority: P1) 🎯 MVP

**Goal**: Developer configures archive mode, timestamp, speed, and loop in Godot Project Settings, presses Play, and the scene immediately replays archive data — zero runtime UI interaction.

**Independent Test**: Set archive mode with a timestamp and speed in Project Settings, press Play, observe scene immediately fetches and displays historical metric data at configured speed.

### Tests for User Story 1 (TDD — TimeCursor.Loop) ⚠️

> **Write these tests FIRST, ensure they FAIL before implementation**

- [x] T012 [P] [US1] Write xUnit test: `TimeCursor.Loop` defaults to `false` in `src/pcp-client-dotnet/tests/PcpClient.Tests/TimeCursorTests.cs`
- [x] T013 [P] [US1] Write xUnit test: `Loop=true` + position past `EndBound` → wraps `Position` to `StartTime` in `src/pcp-client-dotnet/tests/PcpClient.Tests/TimeCursorTests.cs`
- [x] T014 [P] [US1] Write xUnit test: `Loop=false` + position past `EndBound` → position continues advancing (no wrap) in `src/pcp-client-dotnet/tests/PcpClient.Tests/TimeCursorTests.cs`
- [x] T015 [P] [US1] Write xUnit test: `Loop=true` + no `EndBound` set → no wrap (can't loop without known bounds) in `src/pcp-client-dotnet/tests/PcpClient.Tests/TimeCursorTests.cs`
- [x] T016 [US1] Run tests, confirm all 4 new tests FAIL (red phase)

### Implementation for User Story 1

- [x] T017 [US1] Add `Loop` bool property to `TimeCursor` in `src/pcp-client-dotnet/src/PcpClient/TimeCursor.cs` (default: `false`)
- [x] T018 [US1] Add `EndBound` property to `TimeCursor` in `src/pcp-client-dotnet/src/PcpClient/TimeCursor.cs` (set from ArchiveDiscovery results)
- [x] T019 [US1] Add wrap-around logic in `TimeCursor.AdvanceBy()` — if `Loop && newPosition > EndBound` → reset `Position` to `StartTime` in `src/pcp-client-dotnet/src/PcpClient/TimeCursor.cs`
- [x] T020 [US1] Run tests, confirm all 4 new tests PASS (green phase)
- [x] T021 [US1] Implement `PmviewBridgePlugin._EnterTree()` — register all 5 `pmview/*` ProjectSettings with defaults and `AddPropertyInfo()` hints per `contracts/project-settings-schema.md` in `godot-project/addons/pmview-bridge/PmviewBridgePlugin.cs`
- [x] T022 [US1] Implement `PmviewBridgePlugin._ExitTree()` — optional cleanup in `godot-project/addons/pmview-bridge/PmviewBridgePlugin.cs`
- [x] T023 [US1] Add ProjectSettings reads at top of `_ready()` in `godot-project/scripts/scenes/metric_scene_controller.gd` — read all 5 `pmview/*` settings with defaults
- [x] T024 [US1] Apply archive mode logic in `_ready()`: if `mode == 0` (Archive), call `MetricPoller.StartPlayback(timestamp)` and `MetricPoller.SetPlaybackSpeed(speed)` in `godot-project/scripts/scenes/metric_scene_controller.gd`
- [x] T025 [US1] Handle empty timestamp: if `archive_start_timestamp` is empty, compute 24h before current time in `godot-project/scripts/scenes/metric_scene_controller.gd`
- [x] T026 [US1] Add `SetLoop(bool)` method to `MetricPoller` in `godot-project/addons/pmview-bridge/MetricPoller.cs` that sets `TimeCursor.Loop`
- [x] T027 [US1] Pass loop setting from scene controller to `MetricPoller.SetLoop()` in `godot-project/scripts/scenes/metric_scene_controller.gd`
- [x] T028 [US1] Wire `ArchiveDiscovery.DetectTimeBounds()` result to `TimeCursor.EndBound` in `godot-project/addons/pmview-bridge/MetricPoller.cs`

**Checkpoint**: User sets archive mode + timestamp + speed + loop in Project Settings, presses Play, scene immediately replays archive data. Loop wraps around when enabled. All TimeCursor tests pass.

---

## Phase 4: User Story 2 — Configure pmproxy Endpoint (Priority: P2)

**Goal**: Developer sets a custom pmproxy URL in the editor, presses Play, and the scene connects to that endpoint instead of the default.

**Independent Test**: Set a custom pmproxy URL in Project Settings, press Play, verify the scene connects to that endpoint.

### Implementation for User Story 2

- [ ] T029 [US2] Apply endpoint from ProjectSettings in `_ready()` — override TOML default if `pmview/endpoint` differs from default `"http://localhost:44322"` in `godot-project/scripts/scenes/metric_scene_controller.gd`

**Checkpoint**: User changes endpoint in Project Settings, presses Play, scene connects to custom endpoint. Default endpoint still works when unchanged.

---

## Phase 5: User Story 3 — Live Mode Configuration (Priority: P3)

**Goal**: Developer toggles to live mode in the editor, presses Play, and the scene launches in standard live-polling mode.

**Independent Test**: Select live mode in Project Settings, press Play, verify scene polls current metric values. Timestamp and speed fields are ignored.

### Implementation for User Story 3

- [ ] T030 [US3] Apply live mode logic in `_ready()`: if `mode == 1` (Live), call `MetricPoller.ResetToLive()` and skip archive settings in `godot-project/scripts/scenes/metric_scene_controller.gd`

**Checkpoint**: User selects live mode, presses Play, scene polls live metrics. Archive settings are ignored.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verification and cleanup across all stories

- [ ] T031 [P] Grep entire `godot-project/` for any remaining references to `scripts/bridge/` — fix stale paths
- [ ] T032 Run full `dotnet test src/pcp-client-dotnet/PcpClient.sln` — all tests green
- [ ] T033 Run quickstart.md validation — follow the quickstart steps end-to-end in Godot editor

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Empty — no setup needed
- **Foundational (Phase 2, US0)**: BLOCKS all user stories — addon must be restructured first
- **User Story 1 (Phase 3)**: Depends on Phase 2 completion — MVP delivery
- **User Story 2 (Phase 4)**: Depends on Phase 2 + Phase 3 (T023 reads settings in _ready, T029 adds endpoint application)
- **User Story 3 (Phase 5)**: Depends on Phase 2 + Phase 3 (T023 reads settings in _ready, T030 adds live mode branch)
- **Polish (Phase 6)**: Depends on all stories being complete

### User Story Dependencies

- **US0 (P0)**: Foundation — must complete first, blocks all other stories
- **US1 (P1)**: Can start after US0. Creates the EditorPlugin and scene controller settings reads that US2/US3 extend.
- **US2 (P2)**: Can start after US1 (extends the settings read logic in _ready). Independently testable by changing endpoint.
- **US3 (P3)**: Can start after US1 (adds the live mode branch in _ready). Independently testable by selecting live mode.

### Within User Story 1

- Tests (T012–T016) MUST be written and FAIL before implementation (T017–T020)
- TimeCursor changes (T017–T020) before MetricPoller loop wiring (T026–T028)
- EditorPlugin (T021–T022) before scene controller integration (T023–T027)
- Scene controller reads (T023) before mode-specific logic (T024–T025, T027)

### Parallel Opportunities

- T003, T004, T005: File moves can run in parallel
- T012, T013, T014, T015: All test tasks write to same file but different test methods — can be authored in parallel
- T021 (EditorPlugin) and T017–T020 (TimeCursor) touch different projects — can run in parallel
- T031 (grep for stale paths) and T032 (run tests) are independent

---

## Parallel Example: User Story 1

```bash
# TimeCursor TDD tests (all in same test file, different methods):
Task T012: "Default Loop = false test"
Task T013: "Loop wraps past EndBound test"
Task T014: "No-loop continues past EndBound test"
Task T015: "Loop without EndBound does nothing test"

# After tests fail, parallel implementation:
Task T017: "Add Loop property to TimeCursor" (PcpClient)
Task T021: "Implement PmviewBridgePlugin._EnterTree()" (Godot addon)
# These touch different projects and can proceed in parallel
```

---

## Implementation Strategy

### MVP First (User Story 0 + User Story 1)

1. Complete Phase 2: Addon Restructure (US0) — structural foundation
2. Complete Phase 3: Archive Playback (US1) — core value delivery
3. **STOP and VALIDATE**: Archive mode works end-to-end from Project Settings
4. This alone satisfies SC-001 and SC-002 (under 30 seconds, zero runtime interaction)

### Incremental Delivery

1. US0 → Addon restructured, existing scenes work → Foundation ready
2. US1 → Archive playback from Project Settings → **MVP!** (SC-001, SC-002, SC-005)
3. US2 → Custom endpoint in Project Settings → Multi-environment support (SC-003 partial)
4. US3 → Live mode toggle → Full mode switching (SC-003 complete, SC-004)
5. Each story adds value without breaking previous stories

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- TDD is mandatory for TimeCursor.Loop (plan Phase B). EditorPlugin and scene controller are verified manually in Godot.
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
