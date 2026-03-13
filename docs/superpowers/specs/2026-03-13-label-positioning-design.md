# Label Positioning Outside Bezels — Design Spec

**Date:** 2026-03-13
**Status:** Approved

## Problem

Grid zone labels (metric column headers and instance row headers) are currently positioned
inside or on the edge of the bezel, making them impossible to read from any practical
viewing distance once shapes are rendered on top. The zone looks like a black blob with
invisible labels.

## Goal

Match the original pcp-pmview label layout: labels sit *outside* the bezel, grouped with
their zone, so every metric name and instance name is readable from the default camera angle.

## Label Layout

Three label types per zone, each with a distinct position:

| Label | Content example | Position |
|---|---|---|
| Zone title | "Network In", "Per-CPU" | Front edge of bezel (current position — no change) |
| Metric column headers | "Bytes", "Packets", "Errors" | Back edge — beyond the far row of shapes |
| Instance row headers | "eth0", "eth1", "cpu0" | Right side — beyond the rightmost column of shapes |

All labels remain **flat on the ground plane** (current rotation unchanged).

### Column header position (metric names)

```
Z = -(rowCount - 1) × rowSpacing - backEdgeOffset
```

`backEdgeOffset` ~ 1.0 — places the label just past the last row's shape footprint.

### Row header position (instance names)

```
X = (colCount - 1) × colSpacing + shapeWidth + rightEdgeOffset
```

`shapeWidth = 0.8f`, `rightEdgeOffset` ~ 0.5 — places the label just past the rightmost
column's shape footprint.

## Group-Aware Layout Spacing

Rather than a fixed inter-zone gap, `LayoutCalculator` computes the full **group bounding
width** for each zone and uses that as the stride when placing adjacent zones:

```
groupWidth = bezelWidth + rowHeaderReservation + interGroupPadding
nextZone.X  = currentZone.X + currentZone.groupWidth
```

`rowHeaderReservation` is a constant (e.g. `2.0f`) that reserves space for the row header
text. Instance names are short and consistent (`cpu0`, `eth0`) so a fixed reservation is
sufficient — no font measurement required.

Column headers extend the group's Z depth (not X stride), and nothing currently sits behind
grid zones, so no Z-stride change is needed.

## What Changes

### `TscnWriter.cs`

- `WriteGridColumnHeaders()` — compute Z from `rowCount` and `rowSpacing` instead of fixed `-0.8`
- `WriteGridRowHeaders()` — compute X from `colCount`, `colSpacing`, `shapeWidth` instead of
  fixed `-0.8`; place on right side (positive X) instead of left

### `LayoutCalculator.cs`

- Add `RowHeaderReservation` constant
- Compute `groupWidth` per zone (bezel width + reservation + padding)
- Use `groupWidth` as the X stride between adjacent zones instead of fixed `ZoneGap`

### `TscnWriterTests.cs`

- Update expected column header Z positions
- Update expected row header X positions (right side, not left)
- Add test asserting group-width spacing between adjacent grid zones

## What Doesn't Change

- Label rotation (flat ground-plane — no rotation change)
- Bezel size (padding was always for shapes; labels were never inside the intent)
- Foreground zone shape labels ("1m", "5m", "User", "Sys" — separate label type)
- Zone title position (front edge, already outside bezel, works well)
