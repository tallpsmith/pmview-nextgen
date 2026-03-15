# Deterministic Animation Pipeline Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract SceneBinder's frame-dependent animation logic into deterministic, test-callable methods so CI tests never flake on frame timing again.

**Architecture:** Split `_Process` into `AdvanceAnimations(delta)` → `AdvanceRotations(delta)` + `AdvanceInterpolations(delta)`. Tests call these directly with explicit deltas. `_Process` becomes a one-line delegation.

**Tech Stack:** C# (.NET 8.0 / Godot.NET.Sdk 4.6.1), GdUnit4 test framework

**Spec:** `docs/superpowers/specs/2026-03-16-deterministic-animation-pipeline-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs` | Modify | Extract three `internal` methods from `_Process` |
| `src/pmview-bridge-addon/test/SceneBinderTests.cs` | Modify | Rewrite 2 flaky tests + add rotation unit test |

No new files. No new dependencies.

---

## Chunk 1: Extract and Test

### Task 1: Extract AdvanceRotations from _Process

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs:42-63`
- Test: `src/pmview-bridge-addon/test/SceneBinderTests.cs`

- [ ] **Step 1: Write failing test for AdvanceRotations**

Add to `SceneBinderTests.cs` after the `// ── Smooth interpolation` section (before line 360):

```csharp
// ── Deterministic animation advancement ─────────────────────────────

[TestCase]
[RequireGodotRuntime]
public async Task AdvanceRotations_AppliesDeltaScaledRotation()
{
    var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
    var node3D = (Node3D)runner.Scene();
    var binder = new SceneBinder();
    runner.Scene().AddChild(binder);

    var bindable = new PcpBindable();
    var binding = new PcpBindingResource
    {
        MetricName = "test.metric",
        TargetProperty = "rotation_speed",
        SourceRangeMin = 0f, SourceRangeMax = 100f,
        TargetRangeMin = 0f, TargetRangeMax = 360f,
        InitialValue = 50f
    };
    bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { binding };
    node3D.AddChild(bindable);
    binder.BindFromSceneProperties(node3D);

    var rotationBefore = node3D.Rotation.Y;
    binder.AdvanceRotations(1.0f); // 1 second at 180 deg/s
    var rotationAfter = node3D.Rotation.Y;

    AssertThat(rotationAfter).IsNotEqual(rotationBefore);
    await runner.AwaitIdleFrame();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build FAIL — `SceneBinder` has no `AdvanceRotations` method.

- [ ] **Step 3: Extract AdvanceRotations in SceneBinder.cs**

Replace the `_Process` method (lines 42-63) with:

```csharp
public override void _Process(double delta)
{
    AdvanceAnimations((float)delta);
}

/// <summary>
/// Advances all animation state by the given time delta.
/// Called by _Process; also callable directly from tests for deterministic control.
/// </summary>
internal void AdvanceAnimations(float delta)
{
    AdvanceRotations(delta);
    AdvanceInterpolations(delta);
}

/// <summary>
/// Applies delta-scaled rotation to all nodes with active rotation_speed bindings.
/// </summary>
internal void AdvanceRotations(float delta)
{
    foreach (var (node, degreesPerSecond) in _rotationSpeeds)
    {
        if (IsInstanceValid(node))
            node.RotateY(Mathf.DegToRad(degreesPerSecond) * delta);
    }
}

/// <summary>
/// Advances exponential-decay interpolation toward smooth targets
/// and applies the updated values to node properties.
/// </summary>
internal void AdvanceInterpolations(float delta)
{
    var smoothFactor = 1f - MathF.Exp(-delta * SmoothSpeed);
    foreach (var key in _smoothValues.Keys.ToList())
    {
        if (!IsInstanceValid(key.TargetNode))
        {
            _smoothValues.Remove(key);
            continue;
        }
        var (current, target) = _smoothValues[key];
        var next = Mathf.Lerp(current, target, smoothFactor);
        _smoothValues[key] = (next, target);
        ApplyBuiltInProperty(key.TargetNode, key.Resolved.Binding.Property, next);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds (Godot tests can only run in CI/Godot editor, but compilation validates the API).

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs \
        src/pmview-bridge-addon/test/SceneBinderTests.cs
git commit -m "Extract AdvanceRotations/AdvanceInterpolations from _Process

Animation logic now callable with explicit delta for deterministic testing.
_Process delegates to AdvanceAnimations which calls both."
```

---

### Task 2: Rewrite flaky SecondUpdate test

**Files:**
- Modify: `src/pmview-bridge-addon/test/SceneBinderTests.cs:362-390`

- [ ] **Step 1: Rewrite ApplyMetrics_SecondUpdate_DoesNotImmediatelyJumpToTarget**

Replace lines 362-390 with:

```csharp
[TestCase]
[RequireGodotRuntime]
public async Task ApplyMetrics_SecondUpdate_DoesNotImmediatelyJumpToTarget()
{
    var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
    var node3D = (Node3D)runner.Scene();
    var binder = new SceneBinder();
    runner.Scene().AddChild(binder);

    var bindable = new PcpBindable();
    var binding = new PcpBindingResource
    {
        MetricName = "test.metric",
        TargetProperty = "height",
        SourceRangeMin = 0f, SourceRangeMax = 100f,
        TargetRangeMin = 0.2f, TargetRangeMax = 5.0f,
        InitialValue = 0f
    };
    bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { binding };
    node3D.AddChild(bindable);
    binder.BindFromSceneProperties(node3D);

    // First update: snaps since no prior smooth value (source 100 → target 5.0)
    binder.ApplyMetrics(MakeSingularMetrics("test.metric", 100.0));
    binder.AdvanceInterpolations(0.016f); // apply the snapped value
    AssertThat(node3D.Scale.Y).IsEqualApprox(5.0f, 0.01f);

    // Second update: target moves to 0.2 — should NOT immediately snap
    binder.ApplyMetrics(MakeSingularMetrics("test.metric", 0.0));
    AssertThat(node3D.Scale.Y).IsGreater(4.0f);

    await runner.AwaitIdleFrame();
}
```

- [ ] **Step 2: Verify the test builds**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/test/SceneBinderTests.cs
git commit -m "Rewrite SecondUpdate test to use deterministic AdvanceInterpolations

No more AwaitIdleFrame between assertions — drives interpolation with
an explicit 16ms delta instead of hoping the frame loop cooperates."
```

---

### Task 3: Rewrite flaky AfterFrames test

**Files:**
- Modify: `src/pmview-bridge-addon/test/SceneBinderTests.cs:394-425`

- [ ] **Step 1: Rewrite ApplyMetrics_AfterFrames_MovesValueTowardTarget**

Replace lines 394-425 with:

```csharp
[TestCase]
[RequireGodotRuntime]
public async Task ApplyMetrics_AfterFrames_MovesValueTowardTarget()
{
    var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
    var node3D = (Node3D)runner.Scene();
    var binder = new SceneBinder();
    runner.Scene().AddChild(binder);

    var bindable = new PcpBindable();
    var binding = new PcpBindingResource
    {
        MetricName = "test.metric",
        TargetProperty = "height",
        SourceRangeMin = 0f, SourceRangeMax = 100f,
        TargetRangeMin = 0.2f, TargetRangeMax = 5.0f,
        InitialValue = 0f
    };
    bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { binding };
    node3D.AddChild(bindable);
    binder.BindFromSceneProperties(node3D);

    // First update: snap to high value
    binder.ApplyMetrics(MakeSingularMetrics("test.metric", 100.0));
    binder.AdvanceInterpolations(0.016f);

    // Second update: target drops to minimum
    binder.ApplyMetrics(MakeSingularMetrics("test.metric", 0.0));
    var valueAfterUpdate = node3D.Scale.Y;

    // Simulate 0.5s of frames (5 × 100ms) — deterministic, no Task.Delay
    for (int i = 0; i < 5; i++)
        binder.AdvanceInterpolations(0.1f);

    AssertThat(node3D.Scale.Y).IsLess(valueAfterUpdate);

    await runner.AwaitIdleFrame();
}
```

- [ ] **Step 2: Verify the test builds**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/test/SceneBinderTests.cs
git commit -m "Rewrite AfterFrames test with deterministic interpolation loop

Replaces Task.Delay(500) with five explicit 100ms AdvanceInterpolations
calls. Fully deterministic — no frame-rate sensitivity in CI."
```

---

### Task 4: Final verification and push

- [ ] **Step 1: Run full build locally**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests pass (the two rewritten tests won't execute locally since they need Godot runtime, but all .NET tests must be green and the build must succeed).

- [ ] **Step 2: Push and monitor CI**

```bash
git push
```

Launch a background agent to monitor the GitHub Actions run. Both Tier 1 (.NET) and Tier 2 (Godot unit tests) should pass — the two previously-flaky tests should now be deterministic.

- [ ] **Step 3: Commit any final fixups if CI reveals issues**

If the Godot test runner reveals unexpected behaviour (e.g., `AdvanceInterpolations` called on a binder not in the scene tree), fix and push.
