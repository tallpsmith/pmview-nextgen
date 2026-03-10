# Feature Specification: PCP-to-Godot 3D Metrics Bridge

**Feature Branch**: `001-pcp-godot-bridge`
**Created**: 2026-03-05
**Status**: Draft
**Input**: User description: "Bridge PCP performance metrics into live 3D Godot scenes for SRE monitoring"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Live Metric Visualisation (Priority: P1)

An SRE launches the application, points it at a remote metrics endpoint (e.g., a collector host), and immediately sees real performance data rendered as moving 3D objects. The 3D scene updates continuously, giving a living, at-a-glance view of server health without reading tables or graphs.

**Why this priority**: This is the core value proposition. Without live metrics driving a 3D scene, nothing else matters.

**Independent Test**: Can be fully tested by connecting to a running metrics endpoint with known synthetic data and verifying that 3D objects move in response to changing metric values.

**Acceptance Scenarios**:

1. **Given** a running metrics endpoint with active data, **When** the SRE launches the application and provides the endpoint address, **Then** the 3D scene displays objects whose positions/sizes/colours reflect current metric values.
2. **Given** a connected session with live data, **When** metric values change on the endpoint, **Then** the 3D scene updates within 2 seconds to reflect the new values.
3. **Given** a connected session, **When** the metrics endpoint becomes unreachable, **Then** the application displays a clear connectivity warning without crashing, and resumes automatically when the endpoint recovers.

---

### User Story 2 - Metric Discovery and Selection (Priority: P2)

An SRE browses available metrics from the connected endpoint — seeing metric names, descriptions, and instance domains (e.g., per-CPU, per-disk) — and selects which metrics to visualise. This lets the SRE focus on the metrics that matter for their current investigation.

**Why this priority**: Discovery makes the tool useful beyond a fixed dashboard. Without it, only pre-configured metrics are visible.

**Independent Test**: Can be tested by connecting to an endpoint with known metrics and verifying the full list is browsable, searchable, and selectable.

**Acceptance Scenarios**:

1. **Given** a connected metrics endpoint, **When** the SRE requests the list of available metrics, **Then** the system displays metric names, descriptions, and their instance domains.
2. **Given** a list of discovered metrics, **When** the SRE selects a metric, **Then** the system adds it to the active visualisation.
3. **Given** metrics with instance domains (e.g., per-CPU), **When** the SRE selects such a metric, **Then** the system displays all instances (e.g., cpu0, cpu1, cpu2) as distinct visual elements.

---

### User Story 3 - Scene Player with Binding Configuration (Priority: P2)

An SRE launches the Player with a Godot scene and a binding configuration file. The configuration maps scene objects (by name/path) to specific metrics and visual properties — e.g., "the bar named cpu_load_bar: height driven by kernel.all.load, normalised 0-10 to 0-5 units". The Player loads the scene, connects to the metrics endpoint, and drives the scene objects according to the configuration. Different scenes with different configs can be swapped without code changes.

**Why this priority**: The Player + config model is the core runtime architecture. Without it, there's no separation between scene authoring and metric integration, and every scene change requires code changes.

**Independent Test**: Can be tested by providing a known scene and binding config, connecting to an endpoint with synthetic data, and verifying that the correct scene objects are mutated by the correct metrics.

**Acceptance Scenarios**:

1. **Given** a Godot scene and a binding configuration file, **When** the Player loads both and connects to a metrics endpoint, **Then** each configured scene object reflects its bound metric value through the specified visual property.
2. **Given** a binding configuration that maps a metric to an object's height, **When** the metric value changes, **Then** the object's height is updated according to the configured normalisation range.
3. **Given** an invalid binding configuration (e.g., referencing a scene object that doesn't exist), **When** the Player loads it, **Then** the system reports the misconfiguration clearly without crashing, and continues driving any valid bindings.
4. **Given** two different scenes with their own binding configurations, **When** the SRE switches between them, **Then** each scene displays correctly with its own metric mappings.

---

### User Story 4 - Cross-Platform Desktop Experience (Priority: P2)

An SRE on either a Linux workstation or a macOS laptop installs and runs the application with the same functionality and visual fidelity. The application is a native desktop experience — no browser, no web server, no remote desktop required.

**Why this priority**: The primary audience uses Linux; the development team uses macOS. Both must work from day one to avoid a "works on my machine" trap.

**Independent Test**: Can be tested by running the same application binary/package on both Linux and macOS and verifying identical behaviour against the same metrics endpoint.

**Acceptance Scenarios**:

1. **Given** a Linux desktop, **When** the SRE installs and launches the application, **Then** it connects to a metrics endpoint and renders 3D scenes without additional dependencies.
2. **Given** a macOS desktop, **When** the SRE installs and launches the application, **Then** it provides the same functionality and visual output as the Linux version.

---

### User Story 5 - Time Cursor Playback (Priority: P2)

An SRE sets a start point in the past (e.g., "last Monday 09:00") and plays through available metric data at a controllable pace — faster than real-time for review, or paused to inspect a specific moment. This is essential for development (replaying ingested synthetic data on an otherwise idle host) and for production post-incident analysis.

**Why this priority**: The dev environment ingests synthetic data via pmlogsynth, but the local host produces almost no interesting live metrics. Without a time cursor, there is nothing meaningful to visualise during development. The PCP data model naturally supports querying any timespan — "now" is whatever the application defines it to be. This capability is needed from day one, not deferred.

**Independent Test**: Can be tested by ingesting a known week of synthetic data, setting the cursor to the start of that week, and verifying the 3D scene replays the expected metric progression.

**Acceptance Scenarios**:

1. **Given** a metrics endpoint with historical data, **When** the SRE sets a start time in the past and begins playback, **Then** the 3D scene renders metric values from that point forward.
2. **Given** an active playback session, **When** the SRE adjusts the playback speed, **Then** the scene updates at the new pace without data loss or visual artefacts.
3. **Given** an active playback session, **When** the SRE pauses playback, **Then** the scene freezes at the current cursor position and can be resumed.
4. **Given** no explicit time cursor configuration, **When** the SRE launches the application, **Then** the system defaults to "live" mode (wall-clock now).

---

### Edge Cases

- What happens when the metrics endpoint returns no data (empty instance domains)?
- How does the system handle metrics with extremely large or small values that exceed expected normalisation ranges?
- What happens when a metric that is currently visualised is removed from the endpoint (e.g., a disk is detached)?
- How does the system behave when hundreds of instance domains exist (e.g., a machine with 256 CPUs)?
- What happens when the SRE provides an invalid or malformed endpoint address?
- How does the system handle network latency or slow responses from the metrics endpoint?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST connect to a user-specified metrics endpoint and retrieve live performance data.
- **FR-002**: System MUST render retrieved metric values as 3D visual elements that update continuously.
- **FR-003**: System MUST allow the SRE to discover all available metrics and their instance domains from the connected endpoint.
- **FR-004**: System MUST allow the SRE to select which metrics are actively visualised in the 3D scene.
- **FR-005**: System MUST support metrics with multiple instances (per-CPU, per-disk, per-network-interface) and display each instance as a distinct visual element.
- **FR-006**: System MUST normalise metric values to appropriate visual ranges so that the 3D scene remains readable regardless of raw value magnitudes.
- **FR-007**: System MUST load a binding configuration file that maps scene objects to metrics and visual properties (height, colour, size, rotation, etc.).
- **FR-008**: System MUST support swapping scenes and binding configurations without code changes — the Player is data-driven.
- **FR-009**: System MUST display clear status information about connectivity (connected, disconnected, reconnecting).
- **FR-010**: System MUST run as a native desktop application on both Linux and macOS.
- **FR-011**: System MUST gracefully handle endpoint unavailability — displaying warnings and auto-recovering when connectivity is restored.
- **FR-012**: System MUST provide sensible default visualisations so that a new user can see meaningful output without any configuration.
- **FR-013**: System MUST support a controllable time cursor that defines what "now" means for metric queries — defaulting to wall-clock time (live mode) but configurable to any point in the available data range.
- **FR-014**: System MUST allow the SRE to control playback pace (faster than real-time, real-time, paused) when the time cursor is set to a historical start point.
- **FR-015**: System MUST assume a trusted network (no authentication, plain HTTP) for endpoint connectivity. The architecture MUST NOT preclude future addition of authentication and TLS.

### Key Entities

- **Metrics Endpoint**: A network-accessible service that provides performance metrics. Identified by a base URL. The system connects to exactly one endpoint at a time.
- **Metric**: A named performance measurement (e.g., "kernel.all.load", "disk.dev.read") with a description, data type, and semantic meaning (counter, instant, discrete).
- **Instance Domain**: A set of named instances for a metric (e.g., cpu0, cpu1, sda, eth0). Some metrics have no instance domain (singular values).
- **Time Cursor**: The current position in the metric data timeline. Defaults to wall-clock "now" (live mode). Can be set to a historical point and advanced at a controllable pace. Defines what "now" means for all metric queries.
- **Scene**: A 3D Godot scene crafted externally (in the Godot editor or via AI tooling). The scene is designed for pmview-nextgen integration — its objects and behaviours are named/structured so the Player can bind metrics to them. Scene authoring is orthogonal to this feature.
- **Binding Configuration**: A configuration file that maps scene objects and their visual properties to specific metrics. The Player reads this configuration to know which metric drives which object attribute. This is the glue between a Scene and the metrics layer.
- **Player**: The runtime component that loads a Scene and its Binding Configuration, polls the metrics endpoint via the time cursor, and continuously mutates scene object attributes (position, scale, colour, etc.) based on metric values.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An SRE with no prior experience can connect to a metrics endpoint and see live data in 3D within 60 seconds of first launch.
- **SC-002**: The 3D scene reflects metric value changes within 2 seconds of the value changing at the endpoint.
- **SC-003**: The system displays at least 50 concurrent metric instances without noticeable visual degradation or lag.
- **SC-004**: 90% of SRE users can identify a performance anomaly (e.g., CPU spike) from the 3D visualisation faster than from a traditional text-based metrics display.
- **SC-005**: The Player can load any valid scene + binding configuration pair and drive the scene correctly, with no hard-coded metric or scene assumptions.
- **SC-006**: The application runs identically on both Linux and macOS with zero platform-specific workarounds required by the user.

## Out of Scope

The following are explicitly excluded from this feature to prevent scope creep:

- **Alerting and thresholds**: The system visualises metrics; it does not evaluate alert rules or send notifications.
- **Multi-endpoint monitoring**: The system connects to exactly one metrics endpoint at a time. Simultaneous multi-host dashboards are a future feature.
- **Windows support**: Desktop targets are Linux and macOS only.
- **Metric aggregation and math**: The system displays raw metric values (normalised for visual range). Derived metrics (e.g., rate-of-change calculations, cross-metric formulas) are not in scope.
- **Scene authoring**: Creating or editing 3D scenes is a separate concern. Scenes are crafted in the Godot editor (or potentially via AI tooling like godot-mcp in the future) by scene authors who design for pmview-nextgen integration. This feature covers the Player that consumes scenes, not the tooling that creates them.

### Deferred (In-Scope, Lower Priority)

- **Authentication and transport security**: The initial implementation assumes a trusted network (no auth, plain HTTP). The architecture should not preclude adding TLS and authentication support in the future.

## Clarifications

### Session 2026-03-05

- Q: What features are explicitly out of scope? → A: Alerting, multi-endpoint, Windows, and metric aggregation are out. Controllable time cursor is in-scope at P2 priority — essential for dev workflow (synthetic data on idle host) and production post-incident review.
- Q: What is the security posture for endpoint connectivity? → A: Trusted network only (no auth, no TLS) for initial implementation. Architecture should not preclude future auth/TLS support.
- Q: Are scenes pre-crafted, dynamically generated, or both? → A: Scene authoring is orthogonal. The Player loads a Godot scene + a binding configuration file that maps scene objects to metrics. Scene creation (manual in Godot editor, or AI-generated via godot-mcp) is a completely separate concern. The Player is the core of this feature; scene authoring is out of scope.

## Assumptions

- The metrics endpoint is always accessed over the network (even "local" is via localhost). There is no embedded or offline mode.
- The primary metrics system is PCP (Performance Co-Pilot), accessed through its REST gateway. The spec is written generically to avoid implementation lock-in, but the first implementation will target this ecosystem.
- SREs are technically proficient users comfortable with command-line tools and network configuration. The UX should be efficient, not hand-holding.
- The development environment uses containerised synthetic data for reproducible testing. Production use targets real infrastructure endpoints.
- Sensible defaults for visualisation (e.g., load average as bar height) are provided so configuration is optional for common use cases.

## Dependencies

- A running, network-accessible performance metrics endpoint with a REST-compatible interface.
- A containerised development environment that generates deterministic synthetic metrics data for testing and development.
