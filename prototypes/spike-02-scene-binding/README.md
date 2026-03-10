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

## Findings
_To be filled in after running the spike._
