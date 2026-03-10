# Spike 01: Connectivity Proof

**Goal**: Prove C# in Godot can fetch a real PCP metric value from pmproxy via HttpClient.

## How to Run
1. Start dev environment: `cd dev-environment && podman compose up -d`
2. Open Godot, create a scene with a Node, attach ConnectivitySpike.cs
3. Press F5 — check Output panel for metric data

## Expected Output
```
[Spike-01] Starting connectivity proof...
[Spike-01] Got context: 12345
[Spike-01] Fetch result: {"timestamp":...,"values":[...]}
[Spike-01] SUCCESS — connectivity proof complete!
```

## Findings
_To be filled in after running the spike._
