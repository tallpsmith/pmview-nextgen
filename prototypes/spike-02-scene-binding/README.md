# Spike 02: Scene Binding Proof

**Goal**: Prove a real PCP metric value can drive a 3D object property (bar height) in real-time.

## How to Run
1. Start dev environment: `cd dev-environment && podman compose up -d`
2. Open Godot, create a scene with:
   - A Node3D root with SceneBindingSpike.cs attached
   - A child MeshInstance3D named "Bar" with a BoxMesh
3. Press F5 — the bar should grow/shrink based on kernel.all.load

## Expected Behaviour
The bar's Y scale changes every second, tracking the 1-minute load average.

## Actual Output
```
Godot Engine v4.6.1.stable.mono.official.14d19694e - https://godotengine.org
Metal 4.0 - Forward+ - Using Device #0: Apple - Apple M4 Pro (Apple9)

[Spike-02] Starting scene binding proof...
[Spike-02] Connected with context: 208158542
[Spike-02] Load: 0.19 -> Scale.Y: 0.19
[Spike-02] Load: 0.18 -> Scale.Y: 0.19
[Spike-02] Load: 0.16 -> Scale.Y: 0.18
[Spike-02] Load: 0.15 -> Scale.Y: 0.17
[Spike-02] Load: 0.14 -> Scale.Y: 0.17
[Spike-02] Load: 0.13 -> Scale.Y: 0.16
[Spike-02] Load: 0.12 -> Scale.Y: 0.16
[Spike-02] Load: 0.11 -> Scale.Y: 0.15
[Spike-02] Load: 0.10 -> Scale.Y: 0.15
```

## Findings

**Result: SUCCESS** — A live PCP metric drives a 3D object property in real-time.

### What Worked
- Bar's Y scale updates every second, tracking `kernel.all.load` (1-min average)
- The Lerp normalisation maps raw metric values to a visible scale range
- Polling via `_Process` timer + async fetch works smoothly in Godot's game loop
- No visual hitching or frame drops during HTTP polling

### Surprise: Containerised Load Is Tiny
The dev-environment container produces load averages in the `0.10–0.20` range. The
original Lerp source range of `0.0–10.0` mapped this to imperceptible scale changes.
Tightening the source range to `0.0–1.0` made fluctuations clearly visible.

**Impact on production code**: The binding config's `source_range` / `target_range`
normalisation needs sensible defaults or auto-scaling. SREs will need to tune ranges
for their environment — a container running idle and a 64-core box under load produce
very different numbers for the same metric.

### Same HttpClient Namespace Collision
Same fix as Spike 01 — Godot's own `HttpClient` requires `System.Net.Http.HttpClient`
FQN in scripts that run inside Godot. Non-issue for the PcpClient library layer.
