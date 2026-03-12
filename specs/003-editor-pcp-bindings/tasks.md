# Tasks: Editor-Integrated PCP Bindings

**Input**: Design documents from `/specs/003-editor-pcp-bindings/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md

**Tests**: TDD is mandated by the project constitution (Principle II). Test tasks precede implementation within each phase. Godot editor UI tasks are exempt (no xUnit testability for Godot-dependent code).

**Organization**: Tasks grouped by user story. US1 and US2 are both P1 but US1 is the MVP core — US2 enhances US1 with metric browsing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Prepare the pure .NET and Godot layers for the new binding data model.

- [x] T001 Add `InitialValue` field (double, default 0) to MetricBinding record in `src/pcp-godot-bridge/src/PcpGodotBridge/MetricBinding.cs` — update all existing callers to pass the new parameter
- [x] T002 Run existing test suite to confirm T001 doesn't break anything: `dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Extract reusable validation logic from BindingConfigLoader into a standalone BindingValidator. This is the shared foundation that US1 (editor bindings) and US3 (validation display) both depend on.

**CRITICAL**: No user story work can begin until this phase is complete.

### Tests

> **Write these tests FIRST, ensure they FAIL before implementation**

- [x] T003 [P] Write xUnit tests for `BindingValidator.ValidateBinding()` — cover: missing metric name, missing target property, source range min >= max, target range min >= max, instance_name + instance_id both set, duplicate property targets on same node. File: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingValidatorTests.cs`
- [x] T004 [P] Write xUnit tests for `BindingValidator.ValidateProperty()` — cover: built-in property recognised, custom property pass-through, unknown property returns info message. File: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingValidatorTests.cs` (same file, separate test class)

### Implementation

- [x] T005 Extract offline validation rules from `BindingConfigLoader.ValidateBinding()` into a new static `BindingValidator` class in `src/pcp-godot-bridge/src/PcpGodotBridge/BindingValidator.cs` — methods: `ValidateBinding(MetricBinding, HashSet<string> seenNodeProperties)`, `ValidateProperty(string property)`. Returns `List<ValidationMessage>`.
- [x] T006 Refactor `BindingConfigLoader.ValidateBinding()` in `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfigLoader.cs` to delegate to `BindingValidator` — existing BindingConfigLoaderTests must still pass.
- [x] T007 Run full test suite to confirm refactoring is green: `dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln`

**Checkpoint**: BindingValidator extracted and tested. All existing tests pass. Foundation ready for user stories.

---

## Phase 3: User Story 1 — Bind a Metric to a Node Property in the Editor (Priority: P1) 🎯 MVP

**Goal**: Scene authors can attach a PcpBindable script to any Node3D, add PcpBindingResource entries via the inspector, save/reload the scene with full data persistence, and have SceneBinder pick up bindings at runtime (including initial values).

**Independent Test**: Open any scene with a Node3D, attach PcpBindable script, add a binding via inspector, fill in fields, save scene, re-open — confirm all binding values persist. Press Play — confirm SceneBinder finds and applies bindings, including initial_value before metric data arrives.

### Tests

> **Write these tests FIRST, ensure they FAIL before implementation**

- [x] T008 [P] [US1] Write xUnit tests for `PcpBindingConverter.ToMetricBinding()` — cover: all fields map correctly, InstanceId -1 maps to null, empty InstanceName maps to null, InitialValue preserved. File: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/PcpBindingConverterTests.cs`
- [x] T009 [P] [US1] Write xUnit tests for `PcpBindingConverter.ToMetricBinding()` validation integration — cover: converted binding passes through `BindingValidator.ValidateBinding()` correctly for valid and invalid cases. File: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/PcpBindingConverterTests.cs` (same file, separate test class)

### Implementation

- [x] T010 [P] [US1] Create `PcpBindingConverter` static class in `src/pcp-godot-bridge/src/PcpGodotBridge/PcpBindingConverter.cs` — method: `ToMetricBinding(string sceneNode, string metricName, string targetProperty, double sourceRangeMin, double sourceRangeMax, double targetRangeMin, double targetRangeMax, int instanceId, string? instanceName, double initialValue)` returns `MetricBinding`. This is pure .NET (no Godot dependency) so the Godot Resource passes primitive values in.
- [x] T011 [US1] Run tests to confirm T010 is green: `dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln`
- [x] T012 [P] [US1] Create `PcpBindingResource` Godot Resource class in `godot-project/addons/pmview-bridge/PcpBindingResource.cs` — `[Tool]`, `[GlobalClass]`, `Resource` subclass with `[Export]` properties: MetricName (string), TargetProperty (string, use `[Export(PropertyHint.Enum)]` with hint string built from `PropertyVocabulary.BuiltInNames` — e.g. "height,width,depth,scale,rotation_speed,position_y,color_temperature,opacity"), SourceRangeMin/Max (float), TargetRangeMin/Max (float), InstanceName (string), InstanceId (int, default -1), InitialValue (float). The enum hint gives scene authors a dropdown of built-in properties while still allowing typed custom values (FR-008). Include `ToMetricBinding(string nodeName)` method that delegates to `PcpBindingConverter`.
- [x] T013 [P] [US1] Create `PcpBindable` script in `godot-project/addons/pmview-bridge/PcpBindable.cs` — `[Tool]`, `[GlobalClass]`, extends `Node` (attachable to any Node3D). Single `[Export]` property: `PcpBindings` of type `Godot.Collections.Array<PcpBindingResource>`. Include `GetMetricNames()` helper that returns distinct metric names from all bindings.
- [x] T014 [US1] Add `BindFromSceneProperties(Node sceneRoot)` method to `godot-project/addons/pmview-bridge/SceneBinder.cs` — walks scene tree via `FindChildren("*", "PcpBindable")` or checking each node for PcpBindable script, reads PcpBindings arrays, converts to ActiveBinding records via PcpBindingConverter + PropertyVocabulary.Resolve, validates node/property existence (reuse existing `ValidatePropertyExists`), applies InitialValue to target properties immediately. Returns `string[]` of distinct metric names needed for polling.
- [x] T015 [US1] Update `godot-project/scripts/scenes/metric_scene_controller.gd` — add a new code path: after loading a scene, call `SceneBinder.BindFromSceneProperties(scene_root)` to discover and activate bindings from node properties. Keep existing TOML path functional for now (US4 removes it). The controller should try scene properties first; if no bindings found, fall back to TOML config scanning.
- [x] T016 [US1] Verify `[GlobalClass]` registration of `PcpBindingResource` and `PcpBindable` in `godot-project/addons/pmview-bridge/PmviewBridgePlugin.cs` — build the Godot project and confirm both types appear in the editor's Create Resource / Add Script dialogs. No explicit registration code needed (Godot auto-registers `[GlobalClass]` types), but verify the build succeeds with the new classes.

**Checkpoint**: PcpBindable + PcpBindingResource work in the editor inspector. Bindings persist in .tscn. SceneBinder reads them at runtime. Initial values applied. Existing TOML path still works as fallback.

---

## Phase 4: User Story 2 — Browse and Select Metrics from pmproxy (Priority: P1)

**Goal**: Scene authors can click a "Browse Metrics" button in the inspector to open a metric browser dialog that connects to the configured pmproxy endpoint, navigates the metric namespace hierarchy, shows descriptions and instances, and populates binding fields on selection.

**Independent Test**: Configure a pmproxy endpoint in project settings, open a scene with a PcpBindable node, click "Browse Metrics", verify the namespace tree loads, navigate into a namespace, select a metric with instances, confirm MetricName and InstanceName fields are populated on the binding.

**Depends on**: Phase 3 (US1) — needs PcpBindable and PcpBindingResource to exist.

### Implementation

- [x] T017 [P] [US2] Create `MetricBrowserDialog` in `godot-project/addons/pmview-bridge/MetricBrowserDialog.cs` — `[Tool]` class extending `Window`. Creates its own `PcpClient.PcpClientConnection` to the endpoint from ProjectSettings (`pmview/endpoint`). UI: a `Tree` control for namespace hierarchy, a `Label` for metric description, a `VBoxContainer` for instance list (as `OptionButton` or `ItemList`), and Confirm/Cancel buttons. Methods: `OpenForBinding(PcpBindingResource binding)` stores the target binding, loads root namespaces on open. Tree item activation navigates into namespaces (via `PcpClientConnection.GetChildrenAsync`), selecting a leaf calls `DescribeMetricsAsync` and `GetInstanceDomainAsync`. Confirm writes MetricName + InstanceName back to the binding resource.
- [x] T018 [P] [US2] Create `PcpBindingInspectorPlugin` in `godot-project/addons/pmview-bridge/PcpBindingInspectorPlugin.cs` — `[Tool]` class extending `EditorInspectorPlugin`. `_CanHandle()` returns true for nodes that have a child `PcpBindable` or that themselves are `PcpBindable`. `_ParseProperty()` intercepts `TargetProperty` fields to enhance the enum dropdown with detected `@export` vars from the owning node (read node's property list, filter for `PropertyUsageFlags.ScriptVariable`, append to the built-in vocabulary list — FR-008). `_ParseEnd()` adds a "Browse Metrics" `Button` that opens `MetricBrowserDialog`. Store a single `MetricBrowserDialog` instance, lazily created and added to `EditorInterface.Singleton.GetBaseControl()`.
- [x] T019 [US2] Register `PcpBindingInspectorPlugin` in `godot-project/addons/pmview-bridge/PmviewBridgePlugin.cs` — call `AddInspectorPlugin()` in `_EnterTree()`, `RemoveInspectorPlugin()` in `_ExitTree()`.
- [x] T020 [US2] Handle connection errors gracefully in `MetricBrowserDialog` — if `PcpClientConnection.ConnectAsync()` throws `PcpConnectionException`, display error message in the dialog with a "Retry" button instead of crashing. If endpoint is empty/not configured, show "Configure pmproxy endpoint in Project Settings > PCP" message.
- [x] T021 [US2] Support both Live and Archive metric browsing in `MetricBrowserDialog` — read `pmview/mode` from ProjectSettings. In Archive mode, use `/series/` endpoints for metric discovery (available metrics may differ from live). In Live mode, use standard `/pmapi/` endpoints. Reuse connection patterns from existing `MetricBrowser.cs` and `MetricPoller.cs`.

**Checkpoint**: Metric browser dialog opens from inspector, connects to pmproxy, shows namespace tree with descriptions, lists instances for instanced metrics, populates binding fields on confirm. Handles connection errors gracefully.

---

## Phase 5: User Story 3 — Validate Binding Configuration in the Editor (Priority: P2)

**Goal**: The editor validates bindings and shows visual indicators (errors/warnings/valid) directly in the inspector, covering both offline checks (always active) and connected checks (when pmproxy reachable).

**Independent Test**: Create bindings with known errors (invalid ranges, missing properties, duplicate targets) and confirm the inspector shows red/yellow indicators. Create valid bindings and confirm green indicator. With pmproxy running, confirm connected validation catches non-existent metrics and missing instance selections.

**Depends on**: Phase 2 (BindingValidator), Phase 3 (US1 — PcpBindable exists), Phase 4 (US2 — inspector plugin exists).

### Tests

> **Write these tests FIRST, ensure they FAIL before implementation**

- [x] T022 [P] [US3] Write xUnit tests for `BindingValidator.ValidateBindingSet()` — cover: empty set is valid, single valid binding is valid, two bindings targeting same property on same node is error, multiple bindings targeting different properties is valid. File: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingValidatorTests.cs` (add to existing file)

### Implementation

- [x] T023 [US3] Add `ValidateBindingSet(IReadOnlyList<MetricBinding> bindings)` method to `BindingValidator` in `src/pcp-godot-bridge/src/PcpGodotBridge/BindingValidator.cs` — validates the full set: duplicate property targets across bindings (not just per-binding). Returns aggregate `List<ValidationMessage>`.
- [x] T024 [US3] Run tests to confirm T023 is green: `dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln`
- [x] T025 [US3] Add offline validation display to `PcpBindingInspectorPlugin` in `godot-project/addons/pmview-bridge/PcpBindingInspectorPlugin.cs` — in `_ParseEnd()` (after the Browse button), run offline validation: convert all PcpBindingResource entries to MetricBinding via PcpBindingConverter, call `BindingValidator.ValidateBindingSet()`, display results as colored `Label` controls (red=error, yellow=warning, green=valid). Re-validate when properties change (connect to resource `Changed` signal).
- [x] T026 [US3] Add connected validation to `PcpBindingInspectorPlugin` in `godot-project/addons/pmview-bridge/PcpBindingInspectorPlugin.cs` — add a "Validate Against pmproxy" button that creates a temporary `PcpClientConnection`, checks metric existence via `DescribeMetricsAsync()`, checks instance availability via `GetInstanceDomainAsync()`, flags missing instance selection for instanced metrics. Display results alongside offline validation. Handle connection failure gracefully (show "pmproxy unreachable" warning, don't block offline validation).

**Checkpoint**: Inspector shows validation indicators for all binding error categories. Offline validation always active. Connected validation available on demand. All error categories from FR-010 covered.

---

## Phase 6: User Story 4 — Migrate Existing Demo Configs to Editor Properties (Priority: P3)

**Goal**: The two TOML demo configs (`test_bars.toml`, `disk_io_panel.toml`) are migrated to editor-integrated bindings on the scene nodes. TOML files and TOML-loading runtime code are removed.

**Independent Test**: Open `test_bars.tscn` and `disk_io_panel.tscn` in the editor — verify all bindings are present on the correct nodes with correct values. Run both scenes — verify metric visualisation is identical to previous TOML-driven behaviour.

**Depends on**: Phase 3 (US1) — PcpBindable and SceneBinder.BindFromSceneProperties() must work.

### Implementation

- [x] T027 [P] [US4] Migrate `test_bars.tscn` — attach PcpBindable script to each bound node (LoadBar1Min, LoadBar5Min, LoadBar15Min), create PcpBindingResource entries matching the values in `godot-project/bindings/test_bars.toml`. File: `godot-project/scenes/test_bars.tscn`
- [x] T028 [P] [US4] Migrate `disk_io_panel.tscn` — attach PcpBindable script to each bound node (Disk0Read, Disk0Write, Disk1Read, Disk1Write), create PcpBindingResource entries matching the values in `godot-project/bindings/disk_io_panel.toml`. File: `godot-project/scenes/disk_io_panel.tscn`
- [x] T029 [US4] Update `metric_scene_controller.gd` to remove TOML config scanning fallback — the controller now exclusively uses `SceneBinder.BindFromSceneProperties()`. Remove the `res://bindings/` directory scanning, config cycling (TAB key), and `LoadSceneWithBindings(configPath)` calls. Scene loading switches to loading `.tscn` files directly. File: `godot-project/scripts/scenes/metric_scene_controller.gd`
- [x] T030 [US4] Remove `LoadSceneWithBindings(string configPath)` and TOML-related code from `SceneBinder` in `godot-project/addons/pmview-bridge/SceneBinder.cs` — the method, `LogConfigResult()`, and `_currentConfigPath` field are no longer needed. Keep `ApplyMetrics()`, `UnloadCurrentScene()`, `BindFromSceneProperties()`, and all property application logic.
- [x] T031 [US4] Delete the TOML binding files: `godot-project/bindings/test_bars.toml` and `godot-project/bindings/disk_io_panel.toml`. Remove the `godot-project/bindings/` directory.

**Checkpoint**: Both demo scenes run identically using editor-integrated bindings. No TOML files remain. TOML loading code removed from runtime. SC-005 and SC-006 met.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Cleanup and verification across all user stories.

- [ ] T032 Verify Tomlyn NuGet dependency scope — Tomlyn lives in `src/pcp-godot-bridge/src/PcpGodotBridge/PcpGodotBridge.csproj` (used by BindingConfigLoader, which remains in the library). Confirm no Godot runtime code directly imports Tomlyn. The dependency stays in the library but is no longer exercised at runtime since SceneBinder no longer calls BindingConfigLoader.
- [ ] T033 Run full test suite: `dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln` and `dotnet build godot-project/pmview-nextgen.sln` — confirm everything builds and passes
- [ ] T034 Validate quickstart.md workflow end-to-end in the Godot editor (manual): attach PcpBindable, add binding, browse metrics, validate, save/reload, run scene

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1)**: Depends on Phase 2 — MVP, must complete first
- **Phase 4 (US2)**: Depends on Phase 3 — needs PcpBindable/PcpBindingResource
- **Phase 5 (US3)**: Depends on Phases 2 + 3 + 4 — needs BindingValidator + PcpBindable + InspectorPlugin
- **Phase 6 (US4)**: Depends on Phase 3 — needs BindFromSceneProperties()
- **Phase 7 (Polish)**: Depends on all phases complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — no other story dependencies
- **US2 (P1)**: Needs US1's PcpBindable + PcpBindingResource
- **US3 (P2)**: Needs US1's PcpBindable + US2's InspectorPlugin
- **US4 (P3)**: Needs US1's BindFromSceneProperties() — can run parallel with US2/US3

### Parallel Opportunities

```
Phase 2:  T003 ║ T004  (tests in parallel)
Phase 3:  T008 ║ T009  (tests in parallel)
          T010 ║ T012 ║ T013  (converter + resource + bindable in parallel — different files)
Phase 4:  T017 ║ T018  (dialog + inspector plugin in parallel — different files)
Phase 6:  T027 ║ T028  (scene migrations in parallel — different files)
```

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Pure .NET code before Godot-dependent code
- Data model before behaviour
- Core implementation before integration
- Story complete before moving to next priority

---

## Parallel Example: User Story 1

```bash
# Launch tests in parallel:
Task: "PcpBindingConverter.ToMetricBinding() tests in PcpBindingConverterTests.cs"
Task: "PcpBindingConverter validation integration tests in PcpBindingConverterTests.cs"

# Launch models in parallel (after tests pass):
Task: "PcpBindingConverter in PcpBindingConverter.cs"
Task: "PcpBindingResource in PcpBindingResource.cs"
Task: "PcpBindable in PcpBindable.cs"

# Then sequential: SceneBinder → controller → plugin registration
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Add InitialValue to MetricBinding
2. Complete Phase 2: Extract BindingValidator
3. Complete Phase 3: US1 — PcpBindable + PcpBindingResource + SceneBinder adaptation
4. **STOP and VALIDATE**: Attach PcpBindable to a Node3D, add binding via inspector, save/reload, run scene
5. This alone delivers SC-001, SC-003, SC-007

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 → Binding in editor works → MVP!
3. US2 → Metric browsing works → Full authoring workflow
4. US3 → Validation in editor → Confidence before Play
5. US4 → Demo scenes migrated, TOML removed → Clean codebase

### Note on Godot-Dependent Code

Tasks T012–T021 and T025–T031 create/modify Godot-dependent files that cannot be xUnit tested. These require manual testing in the Godot editor. The pure .NET tasks (T003–T011, T022–T024) have full test coverage via xUnit.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- TDD mandated by constitution — test tasks precede implementation
- Godot editor UI tasks exempt from TDD (no xUnit testability)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
