# Quickstart: CPU Stacked Bar

## Prerequisites

- .NET 10 SDK (`/opt/homebrew/bin/dotnet`)
- PATH includes `/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin`

## Build & Test

```bash
# Build everything
dotnet build pmview-nextgen.sln

# Run all non-integration tests
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"
```

## Key Files

| File | What to Change |
|------|---------------|
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs` | CPU colours + StackGroups |
| `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` | Background stacking support + ground extent fix |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs` | Tests for new colours + stack config |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` | Tests for stacking behaviour + updated assertions |

## Verification

After implementation, generate a scene to visually verify:

```bash
# Requires dev-environment stack running
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  -o /tmp/test-scene/scenes/host_view.tscn
```

Open in Godot and confirm:
1. CPU zone shows 1 stacked bar, not 3 separate bars
2. Segments are red (bottom), green (middle), cyan (top)
3. Per-CPU zone shows N stacked bars (1 per CPU core)
