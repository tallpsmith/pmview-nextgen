# Feature Specification: Editor Launch Configuration

**Feature Branch**: `002-editor-launch-config`
**Created**: 2026-03-11
**Status**: Draft
**Input**: User description: "Godot editor plugin for pre-launch world configuration — pmproxy URL, live/archive mode, archive timestamp, playback speed — so scenes work immediately on launch without runtime overlay fumbling."

## User Scenarios & Testing *(mandatory)*

### User Story 0 - Addon Restructure (Priority: P0)

As a developer preparing the project for plugin-based distribution, I want the existing bridge code (MetricPoller, SceneBinder, MetricBrowser) relocated into a proper Godot addon directory structure, so that the plugin is self-contained and can eventually be copied into any Godot project as a single directory.

**Why this priority**: This is a structural prerequisite — a Godot EditorPlugin must live under `addons/<name>/`. Moving the bridge code now avoids cross-directory dependencies between `addons/` and `scripts/bridge/`, and establishes the packaging foundation for everything that follows.

**Independent Test**: Can be fully tested by verifying the existing scenes (disk_io_panel, test_bars) still function identically after the move — same metrics, same bindings, same behaviour. No functional change, only file locations.

**Acceptance Scenarios**:

1. **Given** the bridge code currently lives in `scripts/bridge/`, **When** the restructure is complete, **Then** MetricPoller.cs, SceneBinder.cs, and MetricBrowser.cs reside under `addons/pmview-bridge/` and all existing scenes load and function without modification to their binding configs or GDScript references.
2. **Given** the addon directory exists with a valid `plugin.cfg`, **When** the user opens the Godot editor, **Then** the plugin appears in the Godot Plugin Manager and can be enabled/disabled.
3. **Given** the restructure is complete, **When** the `scripts/bridge/` directory is inspected, **Then** it no longer exists — no stale files left behind.

---

### User Story 1 - Configure Archive Playback Before Launch (Priority: P1)

As a developer testing scene behaviour against historical data, I want to configure archive mode, start timestamp, and playback speed in the Godot editor before pressing Play, so that when the scene launches it immediately begins replaying archive data at the configured speed without any runtime UI interaction.

**Why this priority**: This is the core pain point — having to launch, open the overlay, type a timestamp, and hit play every single time is tedious and error-prone. Eliminating that friction is the entire reason this feature exists.

**Independent Test**: Can be fully tested by setting archive mode with a timestamp and speed in the editor inspector, pressing Play, and observing that the scene immediately begins fetching and displaying historical metric data at the configured speed.

**Acceptance Scenarios**:

1. **Given** the editor plugin is configured with archive mode, a start timestamp of "2026-03-10T00:00:00", and speed 10x, **When** the user presses Play in the Godot editor, **Then** the scene immediately begins fetching archive data from that timestamp and advancing at 10x speed with no user interaction required.
2. **Given** the editor plugin is configured with archive mode and speed 50x, **When** the scene runs for a period, **Then** the time cursor advances 50 seconds of archive time per 1 second of wall-clock time.
3. **Given** the editor plugin is configured with archive mode but no explicit start timestamp, **When** the user presses Play, **Then** the scene begins playback from 24 hours before the current time.
4. **Given** archive mode with Loop enabled, **When** playback reaches the end of available archive data, **Then** the system automatically restarts from the configured start timestamp without user intervention.
5. **Given** archive mode with Loop disabled (default), **When** playback reaches the end of available archive data, **Then** the scene freezes on the last available metric values and stops advancing.

---

### User Story 2 - Configure pmproxy Endpoint (Priority: P2)

As a developer working with different PCP environments (local dev stack, remote staging, production read-only), I want to set the pmproxy URL in the editor before launch, so that I don't have to modify code or config files to switch between environments.

**Why this priority**: Endpoint configuration is important but less frequently changed than playback mode. Most developers will configure it once and leave it, but it still needs to be easily accessible.

**Independent Test**: Can be fully tested by setting a custom pmproxy URL in the editor, pressing Play, and verifying the scene connects to that endpoint.

**Acceptance Scenarios**:

1. **Given** the plugin shows a pmproxy URL field defaulting to "http://localhost:44322", **When** the user changes it to a different URL, **Then** on launch the scene connects to the new endpoint.
2. **Given** the user has not modified the URL field, **When** the scene launches, **Then** it connects to the default endpoint "http://localhost:44322".

---

### User Story 3 - Live Mode Configuration (Priority: P3)

As a developer monitoring real-time metrics, I want to toggle the plugin to live mode in the editor, so that the scene launches in standard live-polling mode without archive playback.

**Why this priority**: Live mode is the existing default behaviour. The plugin simply needs to preserve this as an option, with archive mode being the new default (since archive is the more common development workflow).

**Independent Test**: Can be fully tested by selecting live mode in the editor, pressing Play, and verifying the scene polls current metric values.

**Acceptance Scenarios**:

1. **Given** the plugin is set to live mode, **When** the user presses Play, **Then** the scene immediately begins polling live metrics from pmproxy.
2. **Given** the plugin is set to live mode, **When** the scene launches, **Then** the start timestamp and speed fields are ignored.

---

### Edge Cases

- What happens when the configured pmproxy URL is unreachable at launch? The scene should handle connection failures gracefully with visible feedback (existing error handling applies).
- What happens when the configured archive timestamp is in the future? The system should treat this the same as no data available — the scene displays with no metric values until archive data becomes available at that timestamp.
- What happens when the archive timestamp is older than available archive data? The system fetches whatever is available — existing archive discovery handles bounds detection.
- What happens when speed is set to 0 or a negative value? The plugin should enforce a minimum speed of 0.1x in the editor UI (validation at input time).
- What happens when archive playback reaches the end of available data? By default, playback stops and the scene freezes on the last available values. If "Loop" is enabled, playback restarts from the configured start timestamp automatically.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-014**: The existing bridge code (MetricPoller, SceneBinder, MetricBrowser) MUST be relocated from `scripts/bridge/` into the addon directory structure under `addons/pmview-bridge/`.
- **FR-015**: The addon MUST include a valid `plugin.cfg` manifest so Godot recognises it as an installable plugin.
- **FR-016**: All existing scenes and GDScript references MUST continue to function after the relocation with no behavioural changes.
- **FR-017**: The `scripts/bridge/` directory MUST be removed after migration — no duplicate files.
- **FR-001**: The system MUST provide a Godot editor plugin that exposes world configuration properties in the Godot Inspector panel.
- **FR-002**: The plugin MUST provide a pmproxy URL field, defaulting to "http://localhost:44322".
- **FR-003**: The plugin MUST provide a mode toggle between "Live" and "Archive", defaulting to Archive.
- **FR-004**: When Archive mode is selected, the plugin MUST display a start timestamp field, defaulting to 24 hours before the current time.
- **FR-005**: When Archive mode is selected, the plugin MUST display a playback speed field, defaulting to 10x.
- **FR-006**: The playback speed MUST be constrained to a minimum of 0.1x and maximum of 100x.
- **FR-011**: When Archive mode is selected, the plugin MUST display a "Loop" toggle, defaulting to off (disabled).
- **FR-012**: When archive playback reaches the end of available data and Loop is disabled, the system MUST stop advancing and freeze the scene on the last available metric values.
- **FR-013**: When archive playback reaches the end of available data and Loop is enabled, the system MUST automatically restart playback from the configured start timestamp.
- **FR-007**: When the scene launches, the system MUST apply all configured settings before any metric polling begins — no user interaction required.
- **FR-008**: When in Live mode, the start timestamp and speed fields MUST be hidden or visually disabled in the editor.
- **FR-009**: The plugin configuration MUST be project-global (one configuration shared by all scenes) and persist across editor sessions.
- **FR-010**: The existing runtime playback overlay MUST remain functional as an alternative for users who prefer runtime control, but it is no longer the primary workflow.

### Key Entities

- **PCP-Godot Plugin** (`addons/pmview-bridge/`): The self-contained Godot addon package comprising the bridge nodes (MetricPoller, SceneBinder, MetricBrowser), the editor plugin (world configuration UI), and the plugin manifest. Designed to be distributable as a single directory copy into any Godot project.
- **World Configuration**: The collection of settings (endpoint URL, mode, start timestamp, speed, loop) that define how a scene connects to and replays PCP data. Project-global — all scenes share a single configuration.
- **Playback Mode**: Either Live (real-time polling) or Archive (historical replay with configurable speed). Determines which fetch strategy the metric poller uses.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can go from opening the Godot editor to viewing archive data in under 30 seconds — configure settings in Inspector, press Play, done.
- **SC-002**: Zero runtime UI interactions are required to begin archive playback when pre-configured in the editor.
- **SC-003**: Switching between live and archive modes requires changing a single toggle in the editor — no code or file edits.
- **SC-004**: Configuration persists across editor restarts without manual re-entry.
- **SC-005**: A full day of archive data (24 hours) can be reviewed in under 15 minutes using the speed multiplier.

## Clarifications

### Session 2026-03-11

- Q: Should configuration scope be per-scene or project-global? → A: Project-global — one configuration shared by all scenes.
- Q: What happens when archive playback reaches the end of available data? → A: Stop by default (freeze on last values), with an opt-in Loop toggle that restarts from the configured start timestamp.
- Q: Should addon restructure (moving bridge code to `addons/pmview-bridge/`) be part of this feature scope? → A: Yes — it's a structural prerequisite for the editor plugin and establishes the packaging foundation. Resolves GitHub issue #1.

## Assumptions

- The existing MetricPoller, TimeCursor, and archive playback infrastructure are functionally correct and will be reused — this feature is about configuration UX, not playback engine changes.
- The Godot editor plugin uses Godot's built-in `[Export]` property and `EditorPlugin` mechanisms for Inspector integration — this is the standard Godot pattern for this kind of tooling.
- Archive mode defaults are chosen for developer convenience: most testing involves replaying yesterday's data at accelerated speed.
- The default speed of 10x is a reasonable starting point — fast enough to be useful, slow enough to observe patterns.
- The runtime playback overlay (F3) is retained for backward compatibility but is no longer the recommended workflow.
- The addon restructure moves only the thin Godot-dependent bridge layer. The pure .NET libraries (PcpClient, PcpGodotBridge) remain in `src/` — they are NuGet-distributable and have no Godot dependency.
- Scenes and GDScript remain project-specific (`scenes/`, `scripts/scenes/`) — they are not part of the distributable plugin.
