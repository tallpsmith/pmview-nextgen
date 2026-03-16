# Feature Specification: CPU Stacked Bar

**Feature Branch**: `004-cpu-stacked-bar`
**Created**: 2026-03-16
**Status**: Draft
**Input**: User description: "Replace the current CPU set of 3 bars with a single stacked bar representing all 3. Order: Sys, User, Nice from bottom up, coloured Red, Green, Cyan respectively."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stacked CPU Bar Replaces Separate Bars (Priority: P1)

As an operator viewing the pmview dashboard, I see a single stacked bar for aggregate CPU utilisation instead of three separate bars. The stacked bar shows Sys (red) on the bottom, User (green) in the middle, and Nice (cyan) on top. Each segment's height is proportional to its metric value, and the total bar height represents combined CPU usage.

**Why this priority**: This is the core visual change — without it there's no feature. The stacked bar gives a clearer at-a-glance picture of total CPU load while still showing the breakdown.

**Independent Test**: Can be fully tested by generating a host-view scene and verifying the CPU zone emits a single stacked bar node (instead of 3 separate bars) with the correct segment ordering, colours, and metric bindings.

**Acceptance Scenarios**:

1. **Given** a host-view scene is generated from a Linux profile, **When** the CPU zone is rendered, **Then** it contains a single stacked bar shape instead of three separate bar shapes
2. **Given** a stacked CPU bar is displayed, **When** metric values arrive (e.g., Sys=15, User=40, Nice=5), **Then** the bar shows three vertically stacked segments with heights proportional to those values
3. **Given** a stacked CPU bar is displayed, **When** inspecting segment colours, **Then** the bottom segment (Sys) is red, the middle segment (User) is green, and the top segment (Nice) is cyan
4. **Given** a stacked CPU bar is displayed, **When** all three metric values are zero, **Then** the bar shows at minimum height (not invisible)

---

### User Story 2 - Stacked Bar Animates Smoothly (Priority: P2)

As an operator watching live metrics, when CPU values change, each segment of the stacked bar transitions smoothly rather than jumping, maintaining the same smooth interpolation behaviour the existing bars have.

**Why this priority**: Visual polish — the current bars already animate, so the stacked replacement should too. Without it the display looks janky.

**Independent Test**: Can be tested by feeding changing metric values and verifying each segment height interpolates over time rather than snapping instantly.

**Acceptance Scenarios**:

1. **Given** a stacked CPU bar is displaying live metrics, **When** the Sys value jumps from 10 to 50, **Then** the Sys segment height animates smoothly to the new value over multiple frames
2. **Given** a stacked CPU bar is animating, **When** one segment grows, **Then** the segments above it shift upward smoothly (maintaining stack integrity)

---

### User Story 3 - Per-CPU Background Zone Also Stacked (Priority: P3)

As an operator viewing the per-CPU background zone, each CPU instance also displays a single stacked bar (instead of 3 bars per CPU), using the same Sys/User/Nice ordering and colour scheme.

**Why this priority**: Consistency — if aggregate CPU is stacked, per-CPU should match. But aggregate is the primary view, so this is lower priority.

**Independent Test**: Can be tested by generating a scene with the per-CPU zone and verifying each CPU instance has a single stacked bar with the correct segment bindings.

**Acceptance Scenarios**:

1. **Given** a host with 4 CPUs, **When** the per-CPU zone is rendered, **Then** there are 4 stacked bars (one per CPU) instead of 12 separate bars (3 per CPU)
2. **Given** a per-CPU stacked bar, **When** inspecting its segments, **Then** the ordering and colours match the aggregate CPU stacked bar (Sys red bottom, User green middle, Nice cyan top)

---

### Edge Cases

- What happens when one metric value is zero? The corresponding segment should collapse to zero height, and the segments above should stack directly on top of the remaining segments.
- What happens when all three metrics sum to more than 100%? The bar should clamp total height to the maximum target range — each segment is sized proportionally within the available space.
- What happens when a metric is temporarily unavailable (no data from pmproxy)? The segment should retain its last known value (existing MetricPoller behaviour) or show at minimum height if no value has ever been received.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The CPU zone definition MUST produce a single stacked bar shape instead of three separate bar shapes
- **FR-002**: The stacked bar MUST display segments in bottom-to-top order: Sys, User, Nice
- **FR-003**: The Sys segment MUST be coloured red, the User segment green, and the Nice segment cyan
- **FR-004**: Each segment's height MUST be proportional to its bound metric value, mapped from the source range (0-100) to its portion of the target range
- **FR-005**: The stacked bar MUST maintain a minimum visible height when all values are zero
- **FR-006**: Segment height changes MUST animate using the existing smooth interpolation (exponential decay)
- **FR-007**: Upper segments MUST reposition vertically as lower segments change height (stack integrity)
- **FR-008**: The per-CPU background zone MUST also use stacked bars, one per CPU instance, with the same segment ordering and colours
- **FR-009**: The scene generation pipeline (Host Projector) MUST emit the stacked bar as a single node with sub-segment bindings in the .tscn output

### Key Entities

- **StackedBar**: A composite shape containing multiple vertically stacked segments, each independently bound to a metric. Replaces multiple separate bars.
- **StackSegment**: An individual coloured segment within a stacked bar, with its own metric binding, colour, and proportional height.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The aggregate CPU zone displays exactly 1 stacked bar instead of 3 separate bars
- **SC-002**: All three CPU metrics (Sys, User, Nice) are visually distinguishable by colour within the stacked bar
- **SC-003**: Segment ordering (bottom-to-top: Sys, User, Nice) is correct in every generated scene
- **SC-004**: The per-CPU zone displays N stacked bars (one per CPU) instead of N*3 separate bars
- **SC-005**: Segment height transitions appear smooth to an observer (no visible jumping between values)

## Assumptions

- The existing MetricPoller and SceneBinder data pipeline does not need fundamental changes — the stacked bar building block will integrate with the existing binding resource pattern.
- Colour values (red, green, cyan) will be defined as specific RGB values in the profile definition, replacing the current uniform orange.
- The stacked bar is a new building block (like GroundedBar) rather than a modification to the existing bar.
- A new ShapeType (e.g., StackedBar) will be introduced in the zone/metric definition model to distinguish from individual bars.
