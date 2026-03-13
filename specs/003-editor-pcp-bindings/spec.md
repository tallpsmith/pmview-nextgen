# Feature Specification: Editor-Integrated PCP Bindings

**Feature Branch**: `003-editor-pcp-bindings`
**Created**: 2026-03-11
**Status**: Draft
**Input**: User description: "Extend Godot editor support so PCP bindings can be directly mapped and edited inside the Godot editor via custom node properties, replacing external TOML configuration files."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Bind a Metric to a Node Property in the Editor (Priority: P1)

A scene author selects a Node3D in their scene, opens the inspector, and sees a PCP Bindings section. They add a new binding entry, select a PCP metric from a browsable list (fetched from the configured pmproxy endpoint), choose an instance if the metric has multiple instances, pick a target node property (from a vocabulary of built-in mappings plus custom @export vars), configure source and target value ranges, and optionally set an initial value. The binding is saved as part of the scene's node metadata and persists when the scene is saved.

**Why this priority**: This is the core value proposition — moving binding configuration from hand-edited TOML files into the visual editor where scene authors already work. Without this, nothing else matters.

**Independent Test**: Can be tested by opening any scene with a Node3D, adding a PCP binding via the inspector, saving the scene, re-opening it, and confirming the binding data persists correctly.

**Acceptance Scenarios**:

1. **Given** a scene with a Node3D selected in the editor, **When** the user opens the inspector, **Then** a "PCP Bindings" section appears with controls to add/edit/remove metric bindings.
2. **Given** the user clicks "Add Binding" in the PCP Bindings section, **When** they interact with the metric field, **Then** the system fetches available metrics from the configured pmproxy endpoint and presents them as a browsable/searchable list.
3. **Given** a metric with multiple instances (e.g., per-CPU, per-disk), **When** the user selects that metric, **Then** the available instances are fetched and presented for selection.
4. **Given** the user completes all binding fields, **When** the scene is saved and re-opened, **Then** all binding values are fully preserved in the scene file.
5. **Given** the user sets an initial value for a binding, **When** the scene first loads at runtime (before any metric data arrives), **Then** the target property is set to that initial value.

---

### User Story 2 - Browse and Select Metrics from pmproxy (Priority: P1)

A scene author needs to discover which metrics are available from the PCP data source. The editor provides a metric browser that connects to the pmproxy endpoint configured in Godot's project settings, supports both Live and Archive modes, and allows the user to navigate the metric namespace hierarchy, see metric descriptions, and select a metric for binding. When a metric has instance domains, the available instances are listed with their names.

**Why this priority**: Without metric discovery, the user would have to type metric names by hand — defeating the purpose of editor integration. This is co-equal with US1 in importance.

**Independent Test**: Can be tested by configuring a pmproxy endpoint in project settings, opening the metric browser from any binding field, and verifying metrics are listed with descriptions and instance information.

**Acceptance Scenarios**:

1. **Given** a valid pmproxy endpoint configured in project settings, **When** the user opens the metric browser from a binding field, **Then** the root-level metric namespaces are displayed.
2. **Given** the metric browser is open, **When** the user navigates into a namespace, **Then** child metrics and sub-namespaces are shown with descriptions where available.
3. **Given** a metric with an instance domain, **When** the user selects that metric, **Then** the instance names are fetched and displayed for selection.
4. **Given** pmproxy is unreachable, **When** the user tries to browse metrics, **Then** a clear error message is shown indicating the connection problem.
5. **Given** the project is configured for Archive mode, **When** the user browses metrics, **Then** the metric source reflects the archive context rather than live data.

---

### User Story 3 - Validate Binding Configuration in the Editor (Priority: P2)

A scene author wants confidence that their bindings are correctly configured before pressing Play. The editor validates each binding and displays clear visual indicators (warnings, errors) directly in the inspector. Validation covers: metric name format, source/target range validity, property existence on the target node, instance selection consistency, and duplicate binding detection.

**Why this priority**: Validation prevents frustrating trial-and-error debugging at runtime. It's high value but the system still functions (with runtime errors) without it.

**Independent Test**: Can be tested by creating bindings with known errors (invalid ranges, non-existent properties, duplicate bindings) and confirming the editor shows appropriate warnings/errors.

**Acceptance Scenarios**:

1. **Given** a binding where source_range min >= max, **When** the binding is displayed in the inspector, **Then** an error indicator appears with a message explaining the range is invalid.
2. **Given** a binding referencing a property that does not exist on the target node, **When** the binding is validated, **Then** a warning is shown identifying the missing property.
3. **Given** two bindings on the same node targeting the same property, **When** validation runs, **Then** an error is shown indicating a duplicate binding conflict.
4. **Given** a binding where a metric has instances but no instance is selected, **When** validation runs, **Then** a warning prompts the user to select an instance.
5. **Given** all bindings are correctly configured, **When** the user views the inspector, **Then** a visual indicator confirms the configuration is valid.

---

### User Story 4 - Migrate Existing Demo Configs to Editor Properties (Priority: P3)

The two existing TOML demo binding configurations (`test_bars.toml` and `disk_io_panel.toml`) need to be manually migrated to use the new editor-integrated binding format. This is a one-time migration of the existing demo scenes — no general-purpose TOML import tooling is required. Once migrated, the TOML files and TOML-loading runtime code can be removed.

**Why this priority**: Necessary housekeeping to complete the transition, but the new authoring workflow (US1-US3) delivers value independently. Migration can happen last.

**Independent Test**: Can be tested by opening the two migrated demo scenes, verifying all bindings are present on the correct nodes with the correct values, and confirming the scenes run identically to their previous TOML-driven behaviour.

**Acceptance Scenarios**:

1. **Given** the `test_bars` demo scene, **When** opened in the editor after migration, **Then** all metric bindings previously defined in `test_bars.toml` are present as editor properties on the appropriate nodes.
2. **Given** the `disk_io_panel` demo scene, **When** opened in the editor after migration, **Then** all metric bindings previously defined in `disk_io_panel.toml` are present as editor properties on the appropriate nodes.
3. **Given** both demo scenes are migrated, **When** run at runtime, **Then** they visualise metrics identically to their previous TOML-driven behaviour.

---

### Edge Cases

- What happens when pmproxy is not running or the endpoint changes after bindings are configured? Bindings remain stored on the node; metric browsing shows connection error; runtime behaviour depends on MetricPoller's existing error handling.
- What happens when a bound node is renamed or deleted from the scene tree? Bindings are stored on the node itself, so deletion removes the binding naturally. Rename preserves it since the data travels with the node.
- What happens when a metric that was previously available disappears from pmproxy? The stored binding retains the metric name. Validation shows a warning if the metric cannot be found during a connectivity check.
- What happens to existing TOML binding files after migration? They become dead code. The TOML files and TOML-loading runtime paths can be removed once the demo scenes are migrated.
- What happens when source_range and target_range are identical? This is valid — it means a 1:1 pass-through mapping with no scaling.
- What happens when the user configures an initial_value outside the target_range? The initial_value is applied as-is without clamping — it represents the desired starting state, which may intentionally sit outside the animated range.
- What about metrics with many instances (e.g., per-CPU)? Each node binds to exactly one specific instance. The scene author creates separate nodes for each instance they want to visualise and binds each individually. There is no automatic per-instance node cloning.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each Node3D (and its subtypes) MUST be able to carry zero or more PCP metric binding definitions as part of its scene data. Multiple bindings on a single node are supported provided each targets a different property — two or more bindings targeting the same property on the same node is a validation error.
- **FR-002**: Each binding MUST store: metric name, target property, source range (min/max), target range (min/max), and optional instance selector (name or ID, mutually exclusive).
- **FR-003**: Each binding MUST support an optional `initial_value` field (defaulting to 0) that seeds the target property before live/archive data arrives.
- **FR-004**: The editor MUST provide a browsable metric selector that fetches available metrics from the configured pmproxy endpoint.
- **FR-005**: The metric selector MUST support hierarchical namespace navigation and metric description display.
- **FR-006**: For metrics with instance domains, the editor MUST fetch and present available instances for user selection.
- **FR-007**: The metric selector MUST work with both Live and Archive modes as configured in the project's PCP plugin settings.
- **FR-008**: The target property field MUST offer the built-in property vocabulary (height, width, depth, scale, rotation_speed, position_y, color_temperature, opacity) as well as custom @export properties detected on the node.
- **FR-009**: Binding data MUST persist when the scene is saved and be fully restored when the scene is re-opened.
- **FR-010**: The editor MUST validate bindings in two tiers: (a) **Offline validation** (always active): invalid ranges, missing properties, duplicate bindings on the same property; (b) **Connected validation** (when pmproxy is reachable): metric existence, instance availability, missing instance selection for instanced metrics. Both tiers display errors/warnings in the inspector.
- **FR-011**: The two existing demo configurations (`test_bars.toml` and `disk_io_panel.toml`) MUST be manually migrated to the new editor-integrated binding format. No general-purpose TOML import tooling is required.
- **FR-012**: At runtime, the binding data stored on nodes MUST be readable by the SceneBinder/MetricPoller infrastructure to drive metric visualisation. TOML-based loading is superseded and can be removed.
- **FR-013**: The `initial_value` MUST be applied to the target property when the scene is instantiated, before any metric polling begins.
- **FR-014** *(optional)*: A scene MAY override the project-level endpoint and poll interval via a scene-level mechanism (dedicated node or root-node metadata), falling back to project settings when no override is present. Not required for initial delivery.

### Key Entities

- **PcpBinding**: A single metric-to-property mapping stored on a scene node. Contains: metric name, property target, source range, target range, instance selector (optional), initial value (optional, default 0).
- **PcpBindingSet**: The collection of all PcpBinding entries on a single node. Enforces no duplicate property targets.
- **MetricCatalog**: The browsable tree of available PCP metrics fetched from pmproxy, including namespace hierarchy, descriptions, and instance domain information.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Scene authors can add, edit, and remove PCP metric bindings on any Node3D entirely within the Godot editor inspector — no external file editing required.
- **SC-002**: Metric discovery (browse, search, select) completes within 3 seconds for a pmproxy instance serving up to 1,000 metrics.
- **SC-003**: All binding configuration survives a save/reload cycle with zero data loss.
- **SC-004**: Validation feedback appears within 1 second as the user edits binding fields, catching 100% of the error categories defined in FR-010.
- **SC-005**: The two existing demo scenes (`test_bars`, `disk_io_panel`) run identically after migration to editor bindings — no regression in runtime metric visualisation behaviour.
- **SC-006**: After migration, TOML binding files and TOML-loading code are removed with no impact on runtime behaviour.
- **SC-007**: Initial values are visually applied at runtime before metric data arrives, giving scene authors a predictable starting state.

## Clarifications

### Session 2026-03-11

- Q: Is the per-instance node cloning pattern (auto-clone a node for every instance of a metric) in scope? → A: Out of scope. Each binding targets a single specific metric instance. The scene author (human or automated scene builder) is responsible for creating individual nodes and binding each to the desired instance. No automatic per-instance cloning.
- Q: Where do per-scene overrides for poll interval and endpoint live now that TOML is gone? → A: Scene-level override mechanism (dedicated node or root-node metadata) is supported, falling back to project settings. However, this is optional/low-priority — project settings are sufficient for current demo scenes. The override capability exists for future flexibility but is not a P1 deliverable.
- Q: Should validation distinguish between offline checks and checks requiring pmproxy connectivity? → A: Yes, two tiers. Offline validation (ranges, duplicates, property existence) always runs regardless of connectivity. Connected validation (metric existence, instance availability) runs only when pmproxy is reachable. The editor is always useful even without the data stack running.

## Assumptions

- The existing pmview-bridge addon plugin structure and its project settings (endpoint, mode, archive settings) will be extended rather than replaced.
- The existing MetricBrowser bridge node provides the foundation for metric discovery in the editor UI.
- Binding data will be stored using a Godot-native persistence mechanism (node metadata, custom resources, or `_GetPropertyList()`) — the specific mechanism is an implementation decision.
- The existing PropertyVocabulary mapping is reused for the built-in property dropdown.
- This is a clean migration — TOML-based binding files and their runtime loading code are replaced, not kept as a fallback. Once the demo scenes are migrated, the TOML path is removed.
- The runtime bridge (SceneBinder) will be adapted to read bindings from node properties instead of TOML files.
