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

## Actual Output
```
Godot Engine v4.6.1.stable.mono.official.14d19694e - https://godotengine.org
Metal 4.0 - Forward+ - Using Device #0: Apple - Apple M4 Pro (Apple9)

[Spike-01] Starting connectivity proof...
[Spike-01] Got context: 1760406529
[Spike-01] Fetch result: {"context":1760406529,"timestamp":1773175563.538490110,"values":[{"pmid":"60.2.0","name":"kernel.all.load","instances":[{"instance":1,"value":0.22},{"instance":5,"value":0.47999999},{"instance":15,"value":0.43000001}]}]}
```

## Findings

**Result: SUCCESS** — C#/Godot can fetch live PCP metrics from pmproxy via HTTP.

### What Worked
- pmproxy REST API context creation and metric fetching works as expected
- `kernel.all.load` returns three instances (1-min, 5-min, 15-min load averages)
- JSON response includes pmid, metric name, instance IDs, and float values
- System.Text.Json parses the response without issues

### Surprise: HttpClient Namespace Collision
Godot has its own `HttpClient` class, which collides with `System.Net.Http.HttpClient`.
The fix is to use the fully-qualified name:
```csharp
private static readonly System.Net.Http.HttpClient Http = new();
```
**Impact on production code**: PcpClient library lives in a separate .NET project with no
Godot dependency, so this collision won't affect the library layer. The bridge layer
(Godot scripts that reference System.Net.Http) will need FQN or a `using` alias if
it directly uses HttpClient.
