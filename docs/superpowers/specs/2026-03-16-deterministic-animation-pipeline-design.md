# Deterministic Animation Pipeline for SceneBinder

**Date:** 2026-03-16
**Status:** Approved
**Scope:** `SceneBinder.cs`, `SceneBinderTests.cs`

## Problem

Two `SceneBinderTests` are flaky in CI because they depend on Godot's `_Process` frame loop to drive smooth interpolation. Headless CI runners have unpredictable frame timing, making wall-clock-based assertions (`Task.Delay(500)`, `AwaitIdleFrame`) unreliable.

Failing tests:
- `ApplyMetrics_SecondUpdate_DoesNotImmediatelyJumpToTarget`
- `ApplyMetrics_AfterFrames_MovesValueTowardTarget`

## Root Cause

`SceneBinder._Process` owns all animation state advancement (rotation and value interpolation). Tests cannot control when or how many times it runs, so assertions about intermediate animation states are timing-dependent.

## Design

Extract animation state advancement from `_Process` into three testable methods:

```
_Process(double delta)
  └── AdvanceAnimations(float delta)
        ├── AdvanceRotations(float delta)
        └── AdvanceInterpolations(float delta)
```

`_Process` becomes a one-liner that converts `double delta` and delegates.

### Method Signatures

```csharp
// Combined: what _Process calls. Integration-testable.
internal void AdvanceAnimations(float delta)

// Rotation only: applies delta-scaled RotateY to tracked nodes.
internal void AdvanceRotations(float delta)

// Interpolation only: advances exponential lerp toward smooth targets,
// applies updated values to node properties.
internal void AdvanceInterpolations(float delta)
```

All three are `internal` — test-visible via the existing `InternalsVisibleTo` attribute, public API unchanged.

### Interpolation Math (unchanged)

```csharp
var smoothFactor = 1f - MathF.Exp(-delta * SmoothSpeed);
var next = Mathf.Lerp(current, target, smoothFactor);
```

Exponential decay with frame-rate-independent `SmoothSpeed`. Default 5.0 gives ~0.2s response.

### Test Rewrites

**`ApplyMetrics_SecondUpdate_DoesNotImmediatelyJumpToTarget`:**
1. First `ApplyMetrics(value=100)` — snaps via `SetSmoothTarget` (current=target=5.0)
2. `AdvanceInterpolations(0.016f)` — applies snapped value to node
3. Assert `Scale.Y ≈ 5.0`
4. Second `ApplyMetrics(value=0)` — target moves to 0.2, current stays at 5.0
5. Assert `Scale.Y > 4.0` immediately (no frame needed)

**`ApplyMetrics_AfterFrames_MovesValueTowardTarget`:**
1. First `ApplyMetrics(value=100)` + `AdvanceInterpolations(0.016f)` — snap to 5.0
2. Second `ApplyMetrics(value=0)` — target drops to 0.2
3. Loop: call `AdvanceInterpolations(0.1f)` five times (0.5s simulated)
4. Assert value decreased toward 0.2 after each step

No `Task.Delay`, no `AwaitIdleFrame`, fully deterministic.

### What Doesn't Change

- Exponential decay math
- `SmoothSpeed` export property
- `SetSmoothTarget` first-call-snaps behaviour
- All existing passing tests (pure math, extraction, property application)

### Files Modified

| File | Change |
|------|--------|
| `SceneBinder.cs` | Extract `AdvanceAnimations`, `AdvanceRotations`, `AdvanceInterpolations`; slim `_Process` to one-liner |
| `SceneBinderTests.cs` | Rewrite two flaky tests to use deterministic delta; optionally add rotation unit test |
