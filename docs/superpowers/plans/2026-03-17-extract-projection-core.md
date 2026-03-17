# Extract pmview-projection-core Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the discovery, layout, models, and profiles from `pmview-host-projector` into a standalone `pmview-projection-core` .NET library, so both the CLI and the future standalone app can consume the same projection pipeline.

**Architecture:** Pure refactoring — move 10 source files and 7 test files into a new `net8.0` library project. Update namespaces from `PmviewHostProjector.*` to `PmviewProjectionCore.*`. The Host Projector CLI becomes a thin wrapper that references the core. All 510 existing tests must still pass.

**Tech Stack:** C# (.NET 8.0 library, .NET 10.0 CLI/tests), xUnit

**Spec:** `docs/superpowers/specs/2026-03-17-standalone-app-design.md`

---

## File Map

### New files to create

| File | Purpose |
|------|---------|
| `src/pmview-projection-core/src/PmviewProjectionCore/PmviewProjectionCore.csproj` | Library project (net8.0) |
| `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/PmviewProjectionCore.Tests.csproj` | Test project (net10.0) |

### Files moving from Host Projector → Projection Core

**Source files** (10 files — namespace changes from `PmviewHostProjector.*` to `PmviewProjectionCore.*`):

| Current location | New location |
|-----------------|--------------|
| `src/pmview-host-projector/src/PmviewHostProjector/Models/HostTopology.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Models/HostTopology.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Models/SceneLayout.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Models/ZoneDefinition.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Models/ZoneDefinition.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Discovery/MetricDiscovery.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Discovery/MetricDiscovery.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Layout/LayoutCalculator.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/IHostProfileProvider.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Profiles/IHostProfileProvider.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/HostProfileProvider.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Profiles/HostProfileProvider.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Profiles/LinuxProfile.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/MacOsProfile.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Profiles/MacOsProfile.cs` |
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/SharedZones.cs` | `src/pmview-projection-core/src/PmviewProjectionCore/Profiles/SharedZones.cs` |

**Test files** (7 files — namespace changes from `PmviewHostProjector.Tests.*` to `PmviewProjectionCore.Tests.*`):

| Current location | New location |
|-----------------|--------------|
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Discovery/MetricDiscoveryTests.cs` | `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Discovery/MetricDiscoveryTests.cs` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` | `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Layout/LayoutCalculatorTests.cs` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Models/PlacedStackTests.cs` | `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Models/PlacedStackTests.cs` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/HostProfileProviderTests.cs` | `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/HostProfileProviderTests.cs` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs` | `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/LinuxProfileTests.cs` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/MacOsProfileTests.cs` | `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/MacOsProfileTests.cs` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/TestHelpers/StubPcpClient.cs` | `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/TestHelpers/StubPcpClient.cs` |

### Files staying in Host Projector (modified — add `using PmviewProjectionCore.*`)

| File | Change needed |
|------|--------------|
| `src/pmview-host-projector/src/PmviewHostProjector/Program.cs` | Replace `using PmviewHostProjector.{Discovery,Layout,Models,Profiles}` with `PmviewProjectionCore.*` |
| `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs` | Replace `using PmviewHostProjector.Models` with `PmviewProjectionCore.Models` |
| `src/pmview-host-projector/src/PmviewHostProjector/Emission/SceneEmitter.cs` | Replace `using PmviewHostProjector.Models` with `PmviewProjectionCore.Models` |
| `src/pmview-host-projector/src/PmviewHostProjector/Emission/WorldSetup.cs` | Replace `using PmviewHostProjector.Models` with `PmviewProjectionCore.Models` |
| `src/pmview-host-projector/src/PmviewHostProjector/PmviewHostProjector.csproj` | Add `ProjectReference` to core |

### Files staying in Host Projector tests (modified)

| File | Change needed |
|------|--------------|
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs` | Replace `using PmviewHostProjector.{Models,Layout,Profiles}` with `PmviewProjectionCore.*` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/SceneEmitterTests.cs` | Replace `using PmviewHostProjector.Models` with `PmviewProjectionCore.Models` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/WorldSetupTests.cs` | Replace `using PmviewHostProjector.Models` with `PmviewProjectionCore.Models` |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Scaffolding/ProjectScaffolderTests.cs` | No change expected |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Scaffolding/MainSceneWriterTests.cs` | No change expected |

**Note on transitive dependencies:** After extraction, `PmviewHostProjector.Tests.csproj`
references `PmviewHostProjector.csproj` which references `PmviewProjectionCore.csproj`.
.NET exposes transitive ProjectReference types by default, so the test files can use
`using PmviewProjectionCore.*` without a direct ProjectReference. This is intentional —
the test project tests CLI emission code, not core logic. If this ever breaks (e.g.
someone adds `PrivateAssets="all"`), add a direct ProjectReference to the core.

---

## Chunk 1: Create project structure and wire references

The key insight for safe extraction: create the core project AND wire the Host
Projector's ProjectReference FIRST (while files still exist in both places is
fine — we'll delete originals after verifying). This avoids a broken
intermediate state.

### Task 1: Create pmview-projection-core project and wire Host Projector reference

**Files:**
- Create: `src/pmview-projection-core/src/PmviewProjectionCore/PmviewProjectionCore.csproj`
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/PmviewHostProjector.csproj`

- [ ] **Step 1: Create directory structure**

```bash
mkdir -p src/pmview-projection-core/src/PmviewProjectionCore/{Discovery,Layout,Models,Profiles}
```

- [ ] **Step 2: Create PmviewProjectionCore.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>PmviewProjectionCore</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\pcp-client-dotnet\src\PcpClient\PcpClient.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add ProjectReference to core in PmviewHostProjector.csproj**

Add to the existing `<ItemGroup>`:
```xml
<ProjectReference Include="..\..\..\pmview-projection-core\src\PmviewProjectionCore\PmviewProjectionCore.csproj" />
```

- [ ] **Step 4: Verify both projects build (core is empty, CLI unchanged)**

```bash
dotnet build src/pmview-projection-core/src/PmviewProjectionCore/PmviewProjectionCore.csproj
dotnet build src/pmview-host-projector/src/PmviewHostProjector/PmviewHostProjector.csproj
```
Expected: Both build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/pmview-projection-core/
git add src/pmview-host-projector/src/PmviewHostProjector/PmviewHostProjector.csproj
git commit -m "Add empty pmview-projection-core library project

Foundation for extracting discovery/layout/profiles from host-projector
into a shared net8.0 library. Host Projector already references it."
```

### Task 2: Move model files to core

**Files:**
- Move: `Models/HostTopology.cs`, `Models/SceneLayout.cs`, `Models/ZoneDefinition.cs`

- [ ] **Step 1: Copy model files to core**

```bash
cp src/pmview-host-projector/src/PmviewHostProjector/Models/HostTopology.cs \
   src/pmview-projection-core/src/PmviewProjectionCore/Models/
cp src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs \
   src/pmview-projection-core/src/PmviewProjectionCore/Models/
cp src/pmview-host-projector/src/PmviewHostProjector/Models/ZoneDefinition.cs \
   src/pmview-projection-core/src/PmviewProjectionCore/Models/
```

- [ ] **Step 2: Update namespaces in copied files**

In all three files, change:
```csharp
namespace PmviewHostProjector.Models;
```
to:
```csharp
namespace PmviewProjectionCore.Models;
```

- [ ] **Step 3: Update using directives in Host Projector files that reference models**

In `Program.cs`, `TscnWriter.cs`, `SceneEmitter.cs`, `WorldSetup.cs` — replace
`using PmviewHostProjector.Models` with `using PmviewProjectionCore.Models`.

- [ ] **Step 4: Remove originals from host-projector using git rm**

```bash
git rm -r src/pmview-host-projector/src/PmviewHostProjector/Models/
```

- [ ] **Step 5: Verify both projects build**

```bash
dotnet build src/pmview-projection-core/src/PmviewProjectionCore/PmviewProjectionCore.csproj
dotnet build src/pmview-host-projector/src/PmviewHostProjector/PmviewHostProjector.csproj
```
Expected: Both build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/pmview-projection-core/src/PmviewProjectionCore/Models/
git add src/pmview-host-projector/src/PmviewHostProjector/Program.cs
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/
git commit -m "Move model types to pmview-projection-core

HostTopology, SceneLayout, ZoneDefinition and all supporting records
(PlacedZone, PlacedShape, PlacedStack, Vec3, RgbColour, enums)."
```

### Task 3: Move discovery, layout, and profiles to core

**Files:**
- Move: `Discovery/MetricDiscovery.cs`
- Move: `Layout/LayoutCalculator.cs`
- Move: all 5 files in `Profiles/`

- [ ] **Step 1: Copy files to core**

```bash
cp src/pmview-host-projector/src/PmviewHostProjector/Discovery/MetricDiscovery.cs \
   src/pmview-projection-core/src/PmviewProjectionCore/Discovery/
cp src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs \
   src/pmview-projection-core/src/PmviewProjectionCore/Layout/
cp src/pmview-host-projector/src/PmviewHostProjector/Profiles/*.cs \
   src/pmview-projection-core/src/PmviewProjectionCore/Profiles/
```

- [ ] **Step 2: Update namespaces in all copied files**

Each file needs its namespace and using directives updated:

`Discovery/MetricDiscovery.cs`:
- `namespace PmviewHostProjector.Discovery` → `namespace PmviewProjectionCore.Discovery`
- `using PmviewHostProjector.Models` → `using PmviewProjectionCore.Models`

`Layout/LayoutCalculator.cs`:
- `namespace PmviewHostProjector.Layout` → `namespace PmviewProjectionCore.Layout`
- `using PmviewHostProjector.Models` → `using PmviewProjectionCore.Models`

`Profiles/*.cs` (all 5 files):
- `namespace PmviewHostProjector.Profiles` → `namespace PmviewProjectionCore.Profiles`
- `using PmviewHostProjector.Models` → `using PmviewProjectionCore.Models`

- [ ] **Step 3: Update using directives in Program.cs**

Replace:
```csharp
using PmviewHostProjector.Discovery;
using PmviewHostProjector.Layout;
using PmviewHostProjector.Profiles;
```
With:
```csharp
using PmviewProjectionCore.Discovery;
using PmviewProjectionCore.Layout;
using PmviewProjectionCore.Profiles;
```

- [ ] **Step 4: Remove originals from host-projector using git rm**

```bash
git rm -r src/pmview-host-projector/src/PmviewHostProjector/Discovery/
git rm -r src/pmview-host-projector/src/PmviewHostProjector/Layout/
git rm -r src/pmview-host-projector/src/PmviewHostProjector/Profiles/
```

- [ ] **Step 5: Verify both projects build**

```bash
dotnet build src/pmview-projection-core/src/PmviewProjectionCore/PmviewProjectionCore.csproj
dotnet build src/pmview-host-projector/src/PmviewHostProjector/PmviewHostProjector.csproj
```
Expected: Both build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/pmview-projection-core/src/PmviewProjectionCore/
git add src/pmview-host-projector/src/PmviewHostProjector/Program.cs
git commit -m "Move discovery, layout, and profiles to projection core

MetricDiscovery, LayoutCalculator, and all profile types now live in
the shared pmview-projection-core library."
```

## Chunk 2: Move tests and wire up solution

### Task 4: Create core test project and move test files

**Files:**
- Create: `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/PmviewProjectionCore.Tests.csproj`
- Move: 6 test files + `StubPcpClient.cs`

- [ ] **Step 1: Create test directory structure**

```bash
mkdir -p src/pmview-projection-core/tests/PmviewProjectionCore.Tests/{Discovery,Layout,Models,Profiles,TestHelpers}
```

- [ ] **Step 2: Create PmviewProjectionCore.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>PmviewProjectionCore.Tests</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PmviewProjectionCore\PmviewProjectionCore.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Copy test files to core test project**

```bash
cp src/pmview-host-projector/tests/PmviewHostProjector.Tests/Discovery/MetricDiscoveryTests.cs \
   src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Discovery/
cp src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs \
   src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Layout/
cp src/pmview-host-projector/tests/PmviewHostProjector.Tests/Models/PlacedStackTests.cs \
   src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Models/
cp src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/HostProfileProviderTests.cs \
   src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/
cp src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs \
   src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/
cp src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/MacOsProfileTests.cs \
   src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/
cp src/pmview-host-projector/tests/PmviewHostProjector.Tests/TestHelpers/StubPcpClient.cs \
   src/pmview-projection-core/tests/PmviewProjectionCore.Tests/TestHelpers/
```

- [ ] **Step 4: Update namespaces in all copied test files**

Pattern for each test file — replace ALL occurrences:
- `PmviewHostProjector.Tests` → `PmviewProjectionCore.Tests` (namespace declarations)
- `PmviewHostProjector.Models` → `PmviewProjectionCore.Models`
- `PmviewHostProjector.Discovery` → `PmviewProjectionCore.Discovery`
- `PmviewHostProjector.Layout` → `PmviewProjectionCore.Layout`
- `PmviewHostProjector.Profiles` → `PmviewProjectionCore.Profiles`

`StubPcpClient.cs`:
- `namespace PmviewHostProjector.Tests.TestHelpers` → `namespace PmviewProjectionCore.Tests.TestHelpers`

- [ ] **Step 5: Remove originals from host-projector tests using git rm**

```bash
git rm -r src/pmview-host-projector/tests/PmviewHostProjector.Tests/Discovery/
git rm -r src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/
git rm -r src/pmview-host-projector/tests/PmviewHostProjector.Tests/Models/
git rm -r src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/
git rm -r src/pmview-host-projector/tests/PmviewHostProjector.Tests/TestHelpers/
```

- [ ] **Step 6: Verify core tests build and pass**

```bash
dotnet test src/pmview-projection-core/tests/PmviewProjectionCore.Tests/PmviewProjectionCore.Tests.csproj
```
Expected: All projection core tests pass

- [ ] **Step 7: Commit**

```bash
git add src/pmview-projection-core/tests/
git commit -m "Move projection tests to core test project

Discovery, layout, model, and profile tests now live alongside their
source in pmview-projection-core. StubPcpClient moves with them."
```

### Task 5: Update remaining Host Projector tests

**Files:**
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/SceneEmitterTests.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/WorldSetupTests.cs`

These test files reference types that have moved to the core. They need updated
`using` directives. The types are available via transitive ProjectReference
(PmviewHostProjector.csproj → PmviewProjectionCore.csproj).

- [ ] **Step 1: Read each test file to identify all usings that need updating**

Check for any `using PmviewHostProjector.Models`, `using PmviewHostProjector.Layout`,
`using PmviewHostProjector.Profiles`, or `using PmviewHostProjector.Discovery`.
Replace with `PmviewProjectionCore.*` equivalents.

- [ ] **Step 2: Verify Host Projector tests build and pass**

```bash
dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests/PmviewHostProjector.Tests.csproj
```
Expected: All remaining CLI tests pass

- [ ] **Step 3: Commit**

```bash
git add src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/
git commit -m "Update host-projector test usings for projection core namespace"
```

### Task 6: Update solution files

**Files:**
- Modify: `pmview-nextgen.sln`
- Modify: `src/pmview-host-projector/PmviewHostProjector.sln` (sub-solution)

- [ ] **Step 1: Add core projects to root solution**

Run from the repo root:
```bash
dotnet sln pmview-nextgen.sln add \
  src/pmview-projection-core/src/PmviewProjectionCore/PmviewProjectionCore.csproj \
  src/pmview-projection-core/tests/PmviewProjectionCore.Tests/PmviewProjectionCore.Tests.csproj
```

Note: The solution folder nesting can be adjusted manually if needed — the
existing solution uses nested `src/` and `tests/` solution folders per
component. Match the existing pattern (create a `pmview-projection-core`
solution folder with `src` and `tests` subfolders).

- [ ] **Step 2: Update host-projector sub-solution if needed**

Check `src/pmview-host-projector/PmviewHostProjector.sln` — if it only
references the `PmviewHostProjector.csproj` and `PmviewHostProjector.Tests.csproj`,
the ProjectReference in the `.csproj` handles the core dependency automatically.
No change needed unless the sub-solution has direct references to moved files.

- [ ] **Step 3: Run the full solution build and test**

```bash
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"
```
Expected: ALL tests pass — both the moved core tests and the remaining CLI
tests. Total should still be ~510 (no tests lost, no tests added).

- [ ] **Step 4: Commit**

```bash
git add pmview-nextgen.sln
git add src/pmview-host-projector/PmviewHostProjector.sln
git commit -m "Register projection core projects in solution files"
```

### Task 7: Verify addon DLL bundle is unaffected

**Files:**
- Read (no modify expected): `src/pmview-host-projector/src/PmviewHostProjector/AddonInstaller.cs`
- Read (no modify expected): `src/pmview-host-projector/src/PmviewHostProjector/LibraryBuilder.cs`

- [ ] **Step 1: Verify PmviewProjectionCore.dll is NOT needed in addon bundle**

Read `LibraryBuilder.cs` — it publishes `PcpGodotBridge` and copies 3 DLLs
(PcpClient, PcpGodotBridge, Tomlyn). Generated projects use `PcpBindingResource`,
`PcpBindable`, `MetricPoller`, `SceneBinder` — all addon types. The projection
core is consumed by the CLI and standalone app only, not by generated scenes at
runtime. No change needed.

- [ ] **Step 2: Document the decision (no commit needed if no changes)**

This is a verification step. If everything checks out, move on.

### Task 8: Update ARCHITECTURE.md

**Files:**
- Modify: `docs/ARCHITECTURE.md`

Per the spec (Section "Documentation Updates"), `ARCHITECTURE.md` must reflect
the new `pmview-projection-core` library.

- [ ] **Step 1: Read current ARCHITECTURE.md**

Understand the existing structure and where to add the new library's description.

- [ ] **Step 2: Add pmview-projection-core section**

Add a section describing:
- The library's purpose (shared projection pipeline: discovery, layout, profiles)
- That it's pure .NET (net8.0), no Godot dependencies
- Its position in the dependency graph (depends on PcpClient, consumed by
  Host Projector CLI and future standalone app)
- That it was extracted from the Host Projector

- [ ] **Step 3: Update the dependency diagram if one exists**

Add `PmviewProjectionCore` to any architecture diagrams showing the layer stack.

- [ ] **Step 4: Commit**

```bash
git add docs/ARCHITECTURE.md
git commit -m "Update architecture docs for projection core extraction"
```

### Task 9: Final verification

- [ ] **Step 1: Full clean build and test from root**

```bash
dotnet clean pmview-nextgen.sln
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"
```

Expected: Build succeeded, ALL tests pass, 0 failures. Count should be ~510
(same as before extraction — no tests lost, no tests added).

- [ ] **Step 2: Verify directory structure matches spec**

```bash
find src/pmview-projection-core -name "*.cs" | sort
find src/pmview-host-projector/src/PmviewHostProjector -name "*.cs" | sort
```

Core should have: Discovery/, Layout/, Models/, Profiles/ directories with 10 source files.
Host Projector should have: Emission/, Scaffolding/ directories plus root files
(Program.cs, AddonInstaller.cs, CsprojPatcher.cs, LibraryBuilder.cs). No
Discovery/, Layout/, Models/, or Profiles/ directories remaining.

- [ ] **Step 3: Push branch and open PR**

```bash
git push origin <branch-name>
gh pr create --title "Extract pmview-projection-core library" --body "..."
```

Monitor CI to ensure GitHub Actions passes.
