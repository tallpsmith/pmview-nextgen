# Label Positioning Outside Bezels — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move grid zone column and row headers outside the bezel so they are readable from any practical viewing distance, matching the original pcp-pmview layout.

**Architecture:** Two isolated changes — `TscnWriter` repositions column headers to the back edge and row headers to the right side; `LayoutCalculator` accounts for the row header overhang when computing group widths so adjacent zones never overlap. All three changes are TDD-first.

**Tech Stack:** C# (.NET 10), xUnit — no Godot runtime required. Run tests with `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"` from the repo root. Always prefix the PATH: `export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"`.

---

## Chunk 1: Column headers — back edge

### Task 1: Column header Z at back edge (TDD)

**Files:**
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs:291-306`

- [ ] **Step 1: Write the failing test**

Add this test to `TscnWriterTests` in `TscnWriterTests.cs`:

```csharp
[Fact]
public void Write_GridZone_ColumnHeaders_AreAtBackEdge_BeyondLastRow()
{
    // 2 instances, rowSpacing=2.0 → back edge Z = -(2-1)*2.0 - 1.0 = -3.0
    // 3 metrics, colSpacing=1.5 → columns at X = 0, 1.5, 3.0
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Per_CPU", "Per-CPU", Vec3.Zero,
            3, 1.5f, 2.0f,
            [new PlacedShape("s1", ShapeType.Bar, Vec3.Zero,
                "kernel.percpu.cpu.user", "cpu0", "cpu0",
                new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
            GroundWidth: 5f, GroundDepth: 8f,
            MetricLabels: ["User", "Sys", "Nice"],
            InstanceLabels: ["cpu0", "cpu1"])
    ]);
    var tscn = TscnWriter.Write(layout);

    // X=0, Z=-3 for User; X=1.5, Z=-3 for Sys; X=3, Z=-3 for Nice
    Assert.Contains("0, 0.01, -3", tscn);
    Assert.Contains("1.5, 0.01, -3", tscn);
    Assert.Contains("3, 0.01, -3", tscn);
    // Confirm old inside-bezel position is gone
    Assert.DoesNotContain("0.01, -0.8)", tscn);
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "Write_GridZone_ColumnHeaders_AreAtBackEdge_BeyondLastRow" -v minimal
```

Expected: FAIL — transform still contains `-0.8` not `-3`.

- [ ] **Step 3: Implement back-edge Z in WriteGridColumnHeaders**

In `TscnWriter.cs`, replace `WriteGridColumnHeaders`:

```csharp
private static void WriteGridColumnHeaders(StringBuilder sb, PlacedZone zone)
{
    if (zone.MetricLabels is null || zone.MetricLabels.Count == 0) return;
    var colSpacing = zone.GridColumnSpacing ?? 1.5f;
    var rowCount = zone.InstanceLabels?.Count ?? 1;
    var rowSpacing = zone.GridRowSpacing ?? 2.5f;
    var z = -(rowCount - 1) * rowSpacing - 1.0f;

    for (var i = 0; i < zone.MetricLabels.Count; i++)
    {
        var x = i * colSpacing;
        sb.AppendLine($"[node name=\"{zone.Name}ColLabel{i}\" type=\"Label3D\" parent=\"{zone.Name}\"]");
        sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, {F(x)}, 0.01, {F(z)})");
        sb.AppendLine("pixel_size = 0.008");
        sb.AppendLine("font_size = 24");
        sb.AppendLine($"text = \"{zone.MetricLabels[i]}\"");
        sb.AppendLine("horizontal_alignment = 1");
        sb.AppendLine();
    }
}
```

- [ ] **Step 4: Run all TscnWriter tests to verify pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~TscnWriterTests&FullyQualifiedName!~Integration" -v minimal
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Move grid column headers to back edge outside bezel"
```

---

## Chunk 2: Row headers — right side

### Task 2: Row header X at right side (TDD)

**Files:**
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs:308-323`

- [ ] **Step 1: Write the failing test**

Add to `TscnWriterTests`:

```csharp
[Fact]
public void Write_GridZone_RowHeaders_AreOnRightSide_BeyondLastColumn()
{
    // 3 metrics, colSpacing=1.5, shapeWidth=0.8, rightOffset=0.5
    // → X = (3-1)*1.5 + 0.8 + 0.5 = 4.3
    // 2 instances, rowSpacing=2.0 → Z = 0 for cpu0, Z = -2 for cpu1
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Per_CPU", "Per-CPU", Vec3.Zero,
            3, 1.5f, 2.0f,
            [new PlacedShape("s1", ShapeType.Bar, Vec3.Zero,
                "kernel.percpu.cpu.user", "cpu0", "cpu0",
                new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
            GroundWidth: 5f, GroundDepth: 8f,
            MetricLabels: ["User", "Sys", "Nice"],
            InstanceLabels: ["cpu0", "cpu1"])
    ]);
    var tscn = TscnWriter.Write(layout);

    Assert.Contains("4.3, 0.01, 0", tscn);   // cpu0 at Z=0
    Assert.Contains("4.3, 0.01, -2", tscn);  // cpu1 at Z=-2
    // Confirm old left-side position is gone
    Assert.DoesNotContain("-0.8, 0.01,", tscn);
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "Write_GridZone_RowHeaders_AreOnRightSide_BeyondLastColumn" -v minimal
```

Expected: FAIL — transform still has `-0.8` on the left.

- [ ] **Step 3: Implement right-side X in WriteGridRowHeaders**

In `TscnWriter.cs`, replace `WriteGridRowHeaders`:

```csharp
private static void WriteGridRowHeaders(StringBuilder sb, PlacedZone zone)
{
    if (zone.InstanceLabels is null || zone.InstanceLabels.Count == 0) return;
    var rowSpacing = zone.GridRowSpacing ?? 2.0f;
    var colCount = zone.MetricLabels?.Count ?? 1;
    var colSpacing = zone.GridColumnSpacing ?? 1.5f;
    const float ShapeWidth = 0.8f;
    const float RightEdgeOffset = 0.5f;
    var x = (colCount - 1) * colSpacing + ShapeWidth + RightEdgeOffset;

    for (var i = 0; i < zone.InstanceLabels.Count; i++)
    {
        var z = -(i * rowSpacing);
        sb.AppendLine($"[node name=\"{zone.Name}RowLabel{i}\" type=\"Label3D\" parent=\"{zone.Name}\"]");
        sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, {F(x)}, 0.01, {F(z)})");
        sb.AppendLine("pixel_size = 0.008");
        sb.AppendLine("font_size = 24");
        sb.AppendLine($"text = \"{zone.InstanceLabels[i]}\"");
        sb.AppendLine("horizontal_alignment = 1");
        sb.AppendLine();
    }
}
```

- [ ] **Step 4: Run all TscnWriter tests to verify pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~TscnWriterTests&FullyQualifiedName!~Integration" -v minimal
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Move grid row headers to right side outside bezel"
```

---

## Chunk 3: Group-width-aware layout spacing

### Task 3: Group width includes row header reservation (TDD)

**Files:**
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs:165-172`

Background zones in the scene sit side-by-side on the X axis. With row headers now on the right side of each zone's bezel, the layout must include extra horizontal reservation so adjacent zones never overlap their labels.

- [ ] **Step 1: Write the failing test**

Add to `LayoutCalculatorBackgroundTests` in `LayoutCalculatorTests.cs`:

```csharp
[Fact]
public void Calculate_AdjacentBackgroundZones_HaveGapForRowHeaders()
{
    // Adjacent zones must have at least (bezelWidth + 2.0 reservation) between
    // their start positions so right-side row headers don't overlap the next bezel.
    const float RowHeaderReservation = 2.0f;
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2, nics: 2));
    var background = layout.Zones
        .Where(z => z.GridColumns.HasValue)
        .OrderBy(z => z.Position.X)
        .ToList();

    for (var i = 0; i < background.Count - 1; i++)
    {
        var left = background[i];
        var right = background[i + 1];
        var gap = right.Position.X - left.Position.X;
        Assert.True(gap >= left.GroundWidth + RowHeaderReservation,
            $"Zone '{left.Name}' → '{right.Name}': gap {gap} < {left.GroundWidth + RowHeaderReservation} (bezel + reservation)");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "Calculate_AdjacentBackgroundZones_HaveGapForRowHeaders" -v minimal
```

Expected: FAIL — current `ZoneWidth` does not include row header reservation.

- [ ] **Step 3: Add RowHeaderReservation and update ZoneWidth**

In `LayoutCalculator.cs`:

1. Add constant after the existing constants block (around line 17):

```csharp
private const float RowHeaderReservation = 2.0f;
```

2. Replace `ZoneWidth` (lines 165–172):

```csharp
private static float ZoneWidth(PlacedZone zone)
{
    // Grid zones: shapes are at Vec3.Zero (positioned by GridLayout3D at runtime).
    // Add RowHeaderReservation to account for right-side instance labels.
    if (zone.GridColumns.HasValue && zone.GroundWidth > 0f)
        return zone.GroundWidth + RowHeaderReservation;
    if (zone.Shapes.Count == 0) return 0f;
    return zone.Shapes.Max(s => s.LocalPosition.X);
}
```

- [ ] **Step 4: Run all LayoutCalculator tests to verify pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~LayoutCalculator&FullyQualifiedName!~Integration" -v minimal
```

Expected: all PASS.

- [ ] **Step 5: Run full non-integration suite to catch regressions**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration" -v minimal
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs
git commit -m "Reserve horizontal space for row header labels when spacing grid zones"
```
