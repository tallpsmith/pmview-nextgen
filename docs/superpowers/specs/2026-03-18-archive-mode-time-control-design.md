# Archive Mode & Time Control Design

**Date:** 2026-03-18
**Status:** Approved

## Overview

Two-phase feature bringing full archive playback support to the standalone pmview app, plus a Time Machine-style timeline navigation UI for scrubbing through historical data.

- **Phase 1 — Archive Mode Launch:** Wire the existing Archive button, add source selection and time bounds discovery, branch the loading pipeline for archive topology.
- **Phase 2 — Time Control:** Right-edge translucent overlay with timeline bars, playhead display, IN/OUT range looping, and keyboard scrubbing.

Both phases share `TimeCursor` and `MetricPoller` as the integration point.

## Phase 1: Archive Mode Launch Flow

### Main Menu Changes

The existing `ArchiveButton` in `main_menu.tscn` is currently disabled with no handler. Changes:

1. **Enable ArchiveButton** — remove `disabled = true`.
2. **Archive config panel** — appears when Archive mode is selected, contains:
   - **Source Host dropdown** — populated from `/series/labels?names=hostname`.
   - **Archive Range display** — read-only, shows discovered time bounds after hostname selection.
   - **Start Time input** — ISO 8601 text field, defaults to archive end minus 24 hours (clamped to archive bounds).
3. **Mode toggle behaviour** — switching to Live hides the archive panel; switching to Archive shows it and triggers hostname fetch.

### Source Discovery Strategy

Probing is done via a new `ArchiveSourceDiscovery` component in PcpClient (not directly on `PcpSeriesClient`):

1. **List hostnames:** `GET /series/labels?names=hostname` → returns all known hostnames.
2. **Find series for hostname:** `GET /series/query?expr=kernel.all.load%7Bhostname%3D%3D%22{hostname}%22%7D` — URL-encoded label filter. The hostname filter **requires** full RFC 3986 percent-encoding of `{`, `}`, `"`, `=` characters. pmproxy returns 400 Bad Request otherwise.
3. **Probe time bounds:** `GET /series/values?series={id}&start={30_days_ago}&finish={now}` — wide-window query, read min/max timestamps from returned values. One-time cost per hostname selection.

`PcpSeriesQuery` must be pedantic about URL encoding — pmproxy is not lenient.

### API Findings (from live probing against pmproxy 7.0.3)

| Endpoint | Works | Notes |
|----------|-------|-------|
| `/series/labels?names=hostname` | Yes | Returns all hostnames (archive + live) |
| `/series/query?expr=metric{hostname=="X"}` | Yes | **Only with URL-encoded filter** |
| `/series/labels?names=source` | No | Returns `{}` |
| `/series/labels?series=ID` | No | Returns `[]` |
| `/series/sources` | No | 400 Bad Request |
| `/series/values?series=ID&start=X&finish=Y` | Yes | Epoch seconds with decimals |
| `/series/instances?series=ID` | Yes | Returns `source` hash per series |

### SceneManager Config

`connection_config` dictionary gains new keys:

```gdscript
{
    "endpoint": "http://localhost:44322",
    "mode": "archive",        # "live" or "archive"
    "hostname": "saas-prod-01",  # archive only
    "start_time": "2026-03-17T21:30:46Z"  # archive only, ISO 8601
}
```

### Loading Pipeline Branching

`LoadingPipeline.StartPipeline()` accepts mode from config:

- **Live mode:** Existing path — `PcpClientConnection.ConnectAsync()` → `MetricDiscovery.DiscoverAsync()` → layout → build.
- **Archive mode:** `ArchiveMetricDiscoverer` path — uses `/series/*` endpoints to discover topology for the selected hostname, then same layout → build path.

`RuntimeSceneBuilder.Build()` is unchanged — it builds from a `SceneLayout` regardless of data source.

### Host View Changes

`HostViewController` reads `mode` from `SceneManager.connection_config`. If archive mode, calls `MetricPoller.StartPlayback(start_time)` after the built scene is added to the tree.

## Phase 2: Time Control UI

### Visual Design

A translucent 2D overlay anchored to the right edge of the viewport. The panel is a `CanvasLayer` with a `Control` node using custom `_draw()` for the timeline bars.

**Visual elements:**
- **Timeline bars** — horizontal bars representing timestamps across the archive range. Bars near the mouse cursor extend toward it (length proportional to proximity), creating the Time Machine fan-out effect.
- **Playhead** (orange) — bar at the current playback position, always visible.
- **IN point** (green) — marks the start of the loop range.
- **OUT point** (red) — marks the end of the loop range.
- **Active range bars** (purple, translucent) — timestamps within the IN/OUT range (or all bars when no range is set).
- **Inactive bars** (grey, translucent) — timestamps outside the IN/OUT range.
- **Timestamp tooltip** — follows mouse Y position, displays ISO 8601 timestamp.

The entire panel has translucency — it's a ghostly overlay, not a solid wall.

### Panel Visibility

| Trigger | Behaviour |
|---------|-----------|
| F2 | Toggle panel on/off (override) |
| Mouse → right edge | Show panel (if not dismissed via F2) |
| Mouse ← leaves edge | Hide panel |

### Behaviour on Open/Close

- **Panel opens** → playback pauses, playhead marker shows current position.
- **Panel closes** → playback resumes from current position.
- Scene is **frozen** while the Time Control is open — no live preview on hover.

### Interaction Model

**Timeline interaction (panel visible):**

| Input | Action |
|-------|--------|
| Click | Jump playhead to that timestamp |
| SHIFT+Click (1st) | Set IN point. OUT defaults to archive end. Auto-resume. |
| SHIFT+Click (2nd) | Set OUT point. Auto-resume. |

**Keyboard shortcuts (any time in archive mode, panel not required):**

| Key | Action |
|-----|--------|
| Space | Play/pause toggle |
| Left/Right | Step one poll interval back/forward |
| SHIFT+Left/Right | Jump 5 seconds back/forward |
| R | Reset IN/OUT range, auto-resume playback |
| F2 | Toggle Time Control panel |

### Node Structure

```
TimeControl (CanvasLayer)
  TimelinePanel (Control)
    — Custom _draw() renders timeline bars
    — Handles mouse proximity, hover, click
    — Manages IN/OUT point state
    — Emits: playhead_jumped(timestamp)
    — Emits: range_set(in_time, out_time)
    — Emits: range_cleared()
    — Emits: panel_opened()
    — Emits: panel_closed()
  TimestampTooltip (Label)
    — Follows mouse Y position
    — Shows ISO 8601 timestamp
  PlayheadIndicator (ColorRect)
    — Orange bar at current playback position
```

### Signal Flow

```
TimelinePanel.playhead_jumped → HostViewController → MetricPoller.JumpToTimestamp()
TimelinePanel.range_set       → HostViewController → MetricPoller.SetInOutRange()
TimelinePanel.range_cleared   → HostViewController → MetricPoller.ClearRange()
TimelinePanel.panel_opened    → HostViewController → MetricPoller.PausePlayback()
TimelinePanel.panel_closed    → HostViewController → MetricPoller.ResumePlayback()

MetricPoller.PlaybackPositionChanged → TimelinePanel (updates playhead)
```

## Architecture: What Changes Where

### New Components

| Component | Layer | Language | Purpose |
|-----------|-------|----------|---------|
| `ArchiveSourceDiscovery` | PcpClient | C# | List hosts, probe time bounds. Composes PcpSeriesClient calls. |
| `TimeControl` scene | pmview-app | GDScript | CanvasLayer + TimelinePanel for archive navigation |

### Modified Components

| Component | Changes |
|-----------|---------|
| `PcpSeriesQuery` | RFC 3986 URL encoding for hostname label filter expressions |
| `TimeCursor` | Add `InPoint`/`OutPoint` (nullable DateTime), `StepByInterval(seconds, direction)`. `AdvanceBy()` respects IN/OUT bounds for looping. |
| `MetricPoller` | Add `StepPlayback()`, `JumpToTimestamp()`, `SetInOutRange()`, `ClearRange()` |
| `MainMenuController.gd` | Wire ArchiveButton, add hostname dropdown + time input, archive panel show/hide |
| `main_menu.tscn` | Enable ArchiveButton, add archive config panel nodes |
| `SceneManager.gd` | Pass mode/hostname/start_time in config dict |
| `LoadingController.gd` | Read mode from config |
| `LoadingPipeline.cs` | Branch on mode — archive discovery path |
| `HostViewController.gd` | Start playback if archive mode, connect TimeControl signals, keyboard shortcuts |
| `RuntimeSceneBuilder.cs` | Add TimeControl CanvasLayer to UILayer |

### Unchanged Components

- `SceneBinder` — data-source agnostic
- `PcpBindable` / `PcpBindingResource` — binding model unchanged
- Building blocks (grounded_bar, grounded_cylinder, etc.)
- `LayoutCalculator` — layout from topology unchanged
- `MetricRateConverter` — rate conversion unchanged
- Camera / orbit controls
- Loading screen animation (same 6 phases)

## Deferred

- **Playback speed controls** (0.1x–100x) — TimeCursor already supports it, UI deferred to future iteration.
- **Time picker widget** — ISO 8601 text input for now. Open-source Godot time picker could be evaluated later.
- **Live mode Time Control** — archive mode only. Users should use PCP archive logging and switch to archive mode for historical review.
