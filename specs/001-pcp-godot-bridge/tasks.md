# Tasks: PCP-to-Godot 3D Metrics Bridge

**Input**: Design documents from `/specs/001-pcp-godot-bridge/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included per Constitution Principle II (TDD Non-Negotiable) and project CLAUDE.md mandate. Prototypes exempt.

**Organization**: Tasks grouped by user story. Spikes precede production code per Constitution Principle I.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialisation, dev environment, and solution structure

- [x] T001 Create full directory structure per plan.md: prototypes/spike-01-connectivity/, prototypes/spike-02-scene-binding/, prototypes/spike-03-world-crafting/, src/pcp-client-dotnet/src/PcpClient/, src/pcp-client-dotnet/tests/PcpClient.Tests/, godot-project/scripts/bridge/, godot-project/scripts/scenes/, godot-project/scenes/, godot-project/bindings/, dev-environment/pmlogsynth/configs/
- [x] T002 [P] Create dev-environment/compose.yml with PCP, pmproxy, and Valkey containers; include pmlogsynth synthetic data generation and dev-environment/README.md
- [x] T003 [P] Create .NET solution: src/pcp-client-dotnet/PcpClient.sln with src/PcpClient/PcpClient.csproj (net8.0 classlib, System.Text.Json) and tests/PcpClient.Tests/PcpClient.Tests.csproj (xUnit, references PcpClient)
- [x] T004 [P] Initialise Godot 4.4+ project in godot-project/ with project.godot, pmview-nextgen.csproj (Godot.NET.Sdk, Tomlyn NuGet, ProjectReference to PcpClient), and pmview-nextgen.sln (includes Godot project + PcpClient + tests)
- [x] T005 [P] Create pmlogsynth configuration files in dev-environment/pmlogsynth/configs/ for deterministic synthetic test data (CPU load, disk I/O, network metrics)

**Checkpoint**: Dev environment boots, `dotnet build` succeeds for solution, Godot project opens in editor

---

## Phase 2: Spikes (Constitution Principle I - Prototype-First)

**Purpose**: Validate feasibility before production implementation. Spike code is throwaway per constitution.

**CRITICAL**: No production code may begin until spikes complete and findings are documented.

- [x] T006 Spike 1 - Connectivity proof: create minimal C# script in Godot that fetches a real metric value from pmproxy via HttpClient in prototypes/spike-01-connectivity/
- [x] T007 Spike 2 - Scene binding proof: extend spike to have a real metric value drive a 3D object property (e.g., bar height from kernel.all.load) in prototypes/spike-02-scene-binding/
- [x] T008 Spike 3 - AI World Crafting: evaluate godot-mcp for AI-driven scene generation, document capabilities and limitations in prototypes/spike-03-world-crafting/
- [x] T009 Document spike findings: update specs/001-pcp-godot-bridge/spec.md with learnings, surprises, and any requirement adjustments discovered during spikes

**Checkpoint**: All three spikes complete, findings documented, spec updated. Production implementation may now begin.

---

## Phase 3: Foundational PcpClient Library (TDD)

**Purpose**: Core domain types and exception hierarchy that ALL user stories depend on. Pure .NET, no Godot dependency.

**CRITICAL**: Must complete before any user story implementation.

### Tests (write FIRST, confirm FAILING)

- [x] T010 [P] Write failing tests for PCP domain model records (MetricDescriptor, MetricValue, InstanceValue, Instance, InstanceDomain, MetricType/MetricSemantics/ConnectionState enums) in src/pcp-client-dotnet/tests/PcpClient.Tests/ModelTests.cs
- [x] T011 [P] Write failing tests for exception hierarchy (PcpException, PcpConnectionException, PcpContextExpiredException, PcpMetricNotFoundException) in src/pcp-client-dotnet/tests/PcpClient.Tests/ExceptionTests.cs
- [x] T012 [P] Write failing tests for IPcpClient interface contract and PcpClient construction (constructor with Uri, optional HttpClient, initial state Disconnected, IAsyncDisposable) in src/pcp-client-dotnet/tests/PcpClient.Tests/PcpClientConstructionTests.cs

### Implementation (make tests GREEN)

- [x] T013 [P] Implement PCP domain model records and enums per data-model.md in src/pcp-client-dotnet/src/PcpClient/MetricDescriptor.cs, MetricValue.cs, InstanceDomain.cs
- [x] T014 [P] Implement exception hierarchy per contracts/pcpclient-api.md in src/pcp-client-dotnet/src/PcpClient/Exceptions.cs
- [x] T015 Implement IPcpClient interface and PcpClient class skeleton (constructor, State property, IAsyncDisposable) per contracts/pcpclient-api.md in src/pcp-client-dotnet/src/PcpClient/IPcpClient.cs and src/pcp-client-dotnet/src/PcpClient/PcpClient.cs

**Checkpoint**: `dotnet test` passes. Core types established. All user stories can now begin.

---

## Phase 4: User Story 1 - Live Metric Visualisation (Priority: P1) - MVP

**Goal**: An SRE connects to a metrics endpoint and sees real performance data rendered as moving 3D objects that update continuously.

**Independent Test**: Connect to dev-environment pmproxy with synthetic data; verify 3D objects move in response to changing metric values within 2 seconds.

### Tests for US1 (write FIRST, confirm FAILING)

- [x] T016 [P] [US1] Write failing tests for PcpContext lifecycle (ConnectAsync creates context with polltimeout, DisconnectAsync cleans up, context expiry detection and re-creation) with mocked HTTP in src/pcp-client-dotnet/tests/PcpClient.Tests/PcpContextTests.cs
- [x] T017 [P] [US1] Write failing tests for metric fetching (FetchAsync parses pmproxy JSON response, handles singular and instanced metrics, returns typed MetricValue list) with mocked HTTP in src/pcp-client-dotnet/tests/PcpClient.Tests/MetricFetcherTests.cs
- [x] T018 [P] [US1] Write failing tests for connection resilience (endpoint unreachable returns PcpConnectionException, context expired triggers re-creation, state transitions match data-model.md state diagram) in src/pcp-client-dotnet/tests/PcpClient.Tests/ConnectionResilienceTests.cs

### Implementation for US1

- [x] T019 [US1] Implement PcpContext: ConnectAsync (POST /pmapi/context), context ID tracking, timeout management, DisconnectAsync in src/pcp-client-dotnet/src/PcpClient/PcpContext.cs
- [x] T020 [US1] Implement PcpMetricFetcher: FetchAsync (GET /pmapi/fetch), JSON response parsing with System.Text.Json, instance value extraction in src/pcp-client-dotnet/src/PcpClient/PcpMetricFetcher.cs
- [x] T021 [US1] Wire ConnectAsync, DisconnectAsync, and FetchAsync into PcpClient class (IPcpClient implementation) with connection state management in src/pcp-client-dotnet/src/PcpClient/PcpClient.cs
- [x] T022 [US1] Implement connection resilience: auto-reconnect on context expiry, state transitions per data-model.md, PcpConnectionException on unreachable endpoint in src/pcp-client-dotnet/src/PcpClient/PcpClient.cs
- [x] T023 [US1] Create MetricPoller C# bridge node: polls PcpClient on configurable interval, emits Godot signals with metric values, handles connection lifecycle in godot-project/scripts/bridge/MetricPoller.cs
- [x] T024 [US1] Create minimal binding config TOML loader: parse [meta] section and [[bindings]] array using Tomlyn in godot-project/scripts/bridge/BindingConfigLoader.cs
- [x] T025 [US1] Create SceneBinder: applies metric values to scene node properties using normalisation (source_range to target_range linear interpolation), supports binding vocabulary (height, scale, rotation_speed, color_temperature) in godot-project/scripts/bridge/SceneBinder.cs
- [x] T026 [US1] Create basic test scene with placeholder 3D bar objects for CPU load visualisation in godot-project/scenes/test_bars.tscn
- [x] T027 [US1] Create example binding config mapping test scene objects to kernel.all.load and disk.dev.read metrics in godot-project/bindings/test_bars.toml
- [x] T028 [US1] Create GDScript scene controller that wires MetricPoller signals to SceneBinder updates and displays connection status overlay in godot-project/scripts/scenes/metric_scene_controller.gd

**Checkpoint**: Launch Godot project with dev-environment running. 3D bars move in response to synthetic metric data. Connection status visible. Auto-recovery on pmproxy restart. US1 acceptance scenarios 1-3 validated.

---

## Phase 5: User Story 3 - Scene Player with Binding Configuration (Priority: P2)

**Goal**: The Player loads any scene + binding config pair and drives it data-driven. Different scenes/configs swap without code changes.

**Independent Test**: Provide two different scenes with different binding configs; verify each displays correctly with its own metric mappings.

### Tests for US3 (write FIRST, confirm FAILING)

- [ ] T029 [P] [US3] Write failing tests for full TOML binding config validation: required fields, source_range/target_range validation, instance_filter/instance_id mutual exclusion, unknown property rejection, duplicate node+property detection per contracts/binding-config-schema.md in src/pcp-client-dotnet/tests/PcpClient.Tests/BindingConfigValidationTests.cs
- [ ] T030 [P] [US3] Write failing tests for SceneBinder error handling: missing scene node (warning, continue), missing metric (warning, continue), invalid config (report, don't crash) in godot-project tests or integration test scripts

### Implementation for US3

- [ ] T031 [US3] Extend BindingConfigLoader with full validation per contracts/binding-config-schema.md: required field checks, range validation, mutual exclusion rules, vocabulary validation, error/warning reporting in godot-project/scripts/bridge/BindingConfigLoader.cs
- [ ] T032 [US3] Implement scene swapping: Player can unload current scene+bindings and load a new scene+config pair without restart in godot-project/scripts/bridge/SceneBinder.cs
- [ ] T033 [US3] Create second test scene (e.g., disk I/O focused) with different visual layout in godot-project/scenes/disk_io_panel.tscn
- [ ] T034 [US3] Create binding config for second test scene mapping disk metrics with different normalisation ranges in godot-project/bindings/disk_io_panel.toml
- [ ] T035 [US3] Implement scene/config selection UI: allow SRE to choose from available binding configs at launch or switch at runtime in godot-project/scripts/scenes/config_selector.gd

**Checkpoint**: Two scenes with different configs both work. Invalid bindings reported gracefully. Scene swapping works at runtime. US3 acceptance scenarios 1-4 validated.

---

## Phase 6: User Story 2 - Metric Discovery and Selection (Priority: P2)

**Goal**: An SRE browses available metrics from the endpoint, sees names/descriptions/instance domains, and selects metrics to visualise.

**Independent Test**: Connect to endpoint with known metrics; verify full list is browsable and selectable, with instance domains displayed.

### Tests for US2 (write FIRST, confirm FAILING)

- [ ] T036 [P] [US2] Write failing tests for metric namespace traversal (GetChildrenAsync parses /pmapi/children response, handles leaf and non-leaf nodes) with mocked HTTP in src/pcp-client-dotnet/tests/PcpClient.Tests/MetricDiscoveryTests.cs
- [ ] T037 [P] [US2] Write failing tests for metric description (DescribeMetricsAsync parses /pmapi/metric response including type, semantics, units, help text; PcpMetricNotFoundException for unknown metrics) in src/pcp-client-dotnet/tests/PcpClient.Tests/MetricDiscoveryTests.cs
- [ ] T038 [P] [US2] Write failing tests for instance domain enumeration (GetInstanceDomainAsync parses /pmapi/indom response, returns empty for singular metrics) in src/pcp-client-dotnet/tests/PcpClient.Tests/MetricDiscoveryTests.cs

### Implementation for US2

- [ ] T039 [US2] Implement PcpMetricDiscovery: GetChildrenAsync (PMNS tree walk), DescribeMetricsAsync (metadata fetch), GetInstanceDomainAsync (indom enumeration) in src/pcp-client-dotnet/src/PcpClient/PcpMetricDiscovery.cs
- [ ] T040 [US2] Wire discovery methods into PcpClient class (IPcpClient implementation) in src/pcp-client-dotnet/src/PcpClient/PcpClient.cs
- [ ] T041 [US2] Create metric browser UI: tree view of metric namespace, metric details panel (description, type, instances), select-to-visualise action in godot-project/scripts/scenes/metric_browser.gd and godot-project/scenes/metric_browser.tscn
- [ ] T042 [US2] Display per-instance visual elements: when a metric with instance domains is selected, create distinct 3D objects for each instance (e.g., one bar per CPU) in godot-project/scripts/bridge/SceneBinder.cs

**Checkpoint**: Browse all metrics from dev-environment, see descriptions and instance domains, select a metric and see it visualised with per-instance elements. US2 acceptance scenarios 1-3 validated.

---

## Phase 7: User Story 5 - Time Cursor Playback (Priority: P2)

**Goal**: An SRE sets a start point in the past and replays metric data at controllable pace for dev workflow and post-incident analysis.

**Independent Test**: Ingest known synthetic data week, set cursor to start, verify 3D scene replays expected metric progression.

### Tests for US5 (write FIRST, confirm FAILING)

- [ ] T043 [P] [US5] Write failing tests for TimeCursor state machine (Live/Playback/Paused transitions, PlaybackSpeed clamping 0.1-100.0, Position advancement per speed multiplier) in src/pcp-client-dotnet/tests/PcpClient.Tests/TimeCursorTests.cs
- [ ] T044 [P] [US5] Write failing tests for historical series queries (query /series/query and /series/values endpoints, parse response with series identifiers and timestamped values) with mocked HTTP in src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs

### Implementation for US5

- [ ] T045 [US5] Implement TimeCursor model: mode transitions, position tracking, speed-adjusted advancement per data-model.md in src/pcp-client-dotnet/src/PcpClient/TimeCursor.cs
- [ ] T046 [US5] Implement series query support: /series/query for historical data, /series/values for fetching timestamped values from Valkey backend in src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs
- [ ] T047 [US5] Integrate TimeCursor with MetricPoller: switch between live fetch (FetchAsync) and historical replay (series queries) based on cursor mode in godot-project/scripts/bridge/MetricPoller.cs
- [ ] T048 [US5] Create playback control UI: start time picker, play/pause/resume buttons, speed slider, current position display, reset-to-live button in godot-project/scripts/scenes/playback_controls.gd and godot-project/scenes/playback_controls.tscn

**Checkpoint**: Set cursor to past time, scene replays synthetic data. Speed adjustment works. Pause freezes scene. Default is live mode. US5 acceptance scenarios 1-4 validated.

---

## Phase 8: User Story 4 - Cross-Platform Desktop Experience (Priority: P2)

**Goal**: Application runs identically on Linux and macOS with same functionality and visual fidelity.

**Independent Test**: Run same application on both Linux and macOS against same metrics endpoint; verify identical behaviour.

- [ ] T049 [US4] Verify and fix cross-platform build: ensure `dotnet build` and Godot export produce working binaries on both Linux and macOS in godot-project/export_presets.cfg
- [ ] T050 [US4] Create Godot export presets for Linux (x86_64) and macOS (universal) desktop targets in godot-project/export_presets.cfg
- [ ] T051 [US4] Test and document platform-specific issues: file paths, podman vs native containers, font rendering, and resolution handling in dev-environment/README.md

**Checkpoint**: Application launches and connects to pmproxy on both Linux and macOS. Same scenes render identically. US4 acceptance scenarios 1-2 validated.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that span multiple user stories

- [ ] T052 [P] Add integration tests that run against dev-environment pmproxy (PcpClient end-to-end: connect, discover, fetch, series query) in src/pcp-client-dotnet/tests/PcpClient.Tests/IntegrationTests.cs
- [ ] T053 [P] Performance validation: verify 50+ concurrent metric instances without visual degradation, 30+ FPS sustained, stable memory over 1-hour session per Constitution Principle V
- [ ] T054 [P] Create sensible default binding config (FR-012): a "quick start" config that visualises common metrics (CPU, memory, disk) without user configuration in godot-project/bindings/default.toml
- [ ] T055 Run quickstart.md validation: follow dev setup steps end-to-end on a clean environment, fix any discrepancies

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - start immediately
- **Spikes (Phase 2)**: Depends on Setup (needs dev environment running) - BLOCKS all production code
- **Foundational (Phase 3)**: Depends on Spikes completion - BLOCKS all user stories
- **User Stories (Phase 4-8)**: All depend on Foundational phase completion
- **Polish (Phase 9)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational (Phase 3) - No dependencies on other stories. This IS the MVP.
- **US3 (P2)**: Can start after US1 (Phase 4) - Extends the binding system US1 established
- **US2 (P2)**: Can start after Foundational (Phase 3) - PcpClient discovery is independent. UI integrates with existing scene infrastructure.
- **US5 (P2)**: Can start after US1 (Phase 4) - Extends MetricPoller with historical data path. Requires Valkey in dev environment.
- **US4 (P2)**: Can start after US1 (Phase 4) - Cross-platform validation needs a working application to test

### Within Each User Story

- Tests MUST be written and FAIL before implementation (Constitution Principle II)
- Library layer before bridge layer
- Bridge layer before GDScript scene layer
- Core implementation before UI polish

### Parallel Opportunities

- **Phase 1**: T002, T003, T004, T005 all parallel (different directories)
- **Phase 2**: T006 must precede T007; T008 is independent of T006/T007
- **Phase 3**: T010, T011, T012 parallel (test files); T013, T014 parallel (implementation files)
- **Phase 4**: T016, T017, T018 parallel (test files); T023-T028 mostly sequential (bridge before scene)
- **Phase 6 (US2)**: Can run in parallel with Phase 5 (US3) — independent stories after US1 completes
- **Phase 7 (US5)**: Can overlap with Phase 6 (US2) — library work is independent

---

## Parallel Example: User Story 1

```
# Write all US1 tests in parallel (different files):
T016: PcpContext lifecycle tests
T017: Metric fetcher tests
T018: Connection resilience tests

# Then implement library layer (some parallel):
T019: PcpContext implementation
T020: PcpMetricFetcher implementation (parallel with T019)
T021: Wire into PcpClient (depends on T019, T020)
T022: Connection resilience (depends on T021)

# Then bridge + scene layer (sequential):
T023: MetricPoller bridge node
T024: BindingConfigLoader
T025: SceneBinder
T026: Test scene (parallel with T025)
T027: Example binding config (parallel with T025)
T028: GDScript controller (depends on T023-T027)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Spikes (Constitution Principle I gate)
3. Complete Phase 3: Foundational types
4. Complete Phase 4: User Story 1
5. **STOP and VALIDATE**: 3D bars moving from real metrics? Connection resilience works?
6. Demo: "Look, your servers are alive in 3D"

### Incremental Delivery

1. Setup + Spikes + Foundational -> Foundation ready
2. US1 (P1) -> MVP: live metrics in 3D -> Demo
3. US3 (P2) -> Data-driven scene swapping -> Demo
4. US2 (P2) -> Metric browsing and selection -> Demo
5. US5 (P2) -> Time cursor playback -> Demo
6. US4 (P2) -> Cross-platform validation -> Release candidate
7. Polish -> Production ready

### Suggested MVP Scope

US1 alone (Phases 1-4) is a complete, demonstrable product: connect to pmproxy, see live metrics as 3D objects, auto-recover on disconnection. Everything else adds capability but the core value proposition is proven at US1.

---

## Notes

- [P] tasks = different files, no shared state
- [Story] label maps task to specific user story for traceability
- Spike code (Phase 2) does NOT graduate to production per constitution
- All production code follows TDD: Red (failing test) -> Green (implementation) -> Refactor
- Commit after each task or logical TDD cycle (test + implementation pair)
- Stop at any checkpoint to validate story independently
