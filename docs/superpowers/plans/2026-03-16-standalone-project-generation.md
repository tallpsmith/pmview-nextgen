# Standalone Project Generation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `pmview init` to scaffold a complete Godot project from scratch, and a dual-mode fly/orbit camera that replaces the existing orbit-only camera.

**Architecture:** Chunk 1 strips camera/lights/environment from generated scenes (they move to the project main.tscn). Chunk 2 builds the project scaffolder, CLI subcommands, and fly_orbit_camera.gd. Each chunk is independently shippable.

**Tech Stack:** C# (.NET 8.0 / Godot.NET.Sdk 4.6.1), GDScript, xUnit

**Spec:** `docs/superpowers/specs/2026-03-16-standalone-project-generation-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs` | Modify | Remove camera, lights, environment emission |
| `src/pmview-host-projector/src/PmviewHostProjector/Emission/SceneEmitter.cs` | Modify | Drop camera computation |
| `src/pmview-host-projector/src/PmviewHostProjector/Emission/WorldSetup.cs` | Keep | ComputeCamera reused by ProjectScaffolder |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs` | Modify | Update camera/light assertions |
| `src/pmview-bridge-addon/addons/pmview-bridge/camera_orbit.gd` | Delete | Superseded |
| `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd` | Create | Dual-mode camera controller |
| `src/pmview-host-projector/src/PmviewHostProjector/Scaffolding/ProjectScaffolder.cs` | Create | Generates project.godot, .csproj, .sln, main.tscn |
| `src/pmview-host-projector/src/PmviewHostProjector/Scaffolding/MainSceneWriter.cs` | Create | Emits main.tscn with camera, lights, environment |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Scaffolding/ProjectScaffolderTests.cs` | Create | Tests for project generation |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Scaffolding/MainSceneWriterTests.cs` | Create | Tests for main.tscn emission |
| `src/pmview-host-projector/src/PmviewHostProjector/Program.cs` | Modify | Add init subcommand, --init flag |

---

## Chunk 1: Strip Camera/Lights/Environment from Generated Scenes

### Task 1: Remove camera emission from TscnWriter

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Update camera tests to assert camera is NEVER emitted**

In `TscnWriterTests.cs`, replace the three camera tests (lines 279-303) with:

```csharp
// --- Camera tests (camera now lives in project main.tscn, not per-scene) ---

[Fact]
public void Write_NeverEmitsCameraNode()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.DoesNotContain("Camera3D", tscn);
    Assert.DoesNotContain("camera_orbit", tscn);
}
```

- [ ] **Step 2: Run tests to verify the new test fails (camera still emitted via SceneEmitter)**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~TscnWriterTests" --filter "FullyQualifiedName!~Integration"`
Expected: `Write_NeverEmitsCameraNode` may pass (TscnWriter only emits camera when `camera` param is non-null, and MinimalLayout calls `Write` with default null). The old camera tests should now be gone. Verify build succeeds.

- [ ] **Step 3: Remove camera parameter and emission from TscnWriter.cs**

In `TscnWriter.cs`:

1. Remove the `CameraSetup? camera = null` parameter from `Write()` (line 18)
2. Remove the `if (camera != null)` block registering `camera_orbit_script` (lines 23-25)
3. Remove the `camera` parameter from `WriteNodes()` call (line 34) and signature (line 178)
4. Remove the `if (camera != null) WriteCameraNode(sb, camera);` call (lines 219-220)
5. Delete the `WriteCameraNode` method (lines 260-269)
6. Delete the `BuildLookAtTransform` method (lines 271-280) — move to `WorldSetup.cs` for reuse by MainSceneWriter
7. Delete the `Normalise` vector helper (lines 282-286) — move to `WorldSetup.cs`

Move `BuildLookAtTransform` and `Normalise` to `WorldSetup.cs` as `public static` methods.

- [ ] **Step 4: Update SceneEmitter to stop computing camera**

In `SceneEmitter.cs`, simplify `Emit()`:

```csharp
public static string Emit(SceneLayout layout,
    string pmproxyEndpoint = "http://localhost:44322")
{
    return TscnWriter.Write(layout, pmproxyEndpoint);
}
```

Remove the `WorldSetup.ComputeCamera` call and `SceneBounds` computation. (Keep `WorldSetup` and `SceneBounds` — they'll be used by ProjectScaffolder in Chunk 2.)

- [ ] **Step 5: Run all tests**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All pass. Any test that was passing a `camera:` argument to `TscnWriter.Write()` will need updating.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Remove camera/lights/environment ownership from generated scenes

Camera, lighting, and WorldEnvironment will live in the project's
main.tscn (scaffolded by pmview init). Generated host-view scenes
now contain only metric groups, labels, and bindings.

BuildLookAtTransform moved to WorldSetup for reuse by MainSceneWriter."
```

---

### Task 2: Remove lights and WorldEnvironment from TscnWriter

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Add test asserting lights and environment are absent**

```csharp
[Fact]
public void Write_DoesNotEmitLightsOrEnvironment()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.DoesNotContain("KeyLight", tscn);
    Assert.DoesNotContain("FillLight", tscn);
    Assert.DoesNotContain("WorldEnvironment", tscn);
    Assert.DoesNotContain("world_env", tscn);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~DoesNotEmitLightsOrEnvironment"`
Expected: FAIL — lights and WorldEnvironment are still emitted.

- [ ] **Step 3: Remove lights, WorldEnvironment emission from TscnWriter**

In `TscnWriter.cs`:
1. Delete `WriteWorldEnvironmentSubResource()` method (lines 165-174)
2. Remove its call in `Write()` (line 33)
3. In `WriteNodes()`, remove the WorldEnvironment node block (lines 197-199)
4. Remove KeyLight node block (lines 201-206)
5. Remove FillLight node block (lines 208-212)
6. Adjust `load_steps` calculation: remove the `+ 1` for the WorldEnvironment sub_resource (line 105)

- [ ] **Step 4: Run all tests**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Remove lights and WorldEnvironment from generated scenes

These now belong to the project main.tscn. Generated host-view scenes
are pure metric content — lighter and composable."
```

---

### Task 3: Delete camera_orbit.gd

**Files:**
- Delete: `src/pmview-bridge-addon/addons/pmview-bridge/camera_orbit.gd`

- [ ] **Step 1: Delete the file**

```bash
rm src/pmview-bridge-addon/addons/pmview-bridge/camera_orbit.gd
```

- [ ] **Step 2: Verify build still passes**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds (nothing references camera_orbit.gd from C#).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Delete camera_orbit.gd — superseded by fly_orbit_camera.gd"
```

---

## Chunk 2: Project Scaffolding + Dual-Mode Camera

### Task 4: Create fly_orbit_camera.gd

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd`

- [ ] **Step 1: Write the dual-mode camera controller**

```gdscript
extends Camera3D

## Dual-mode camera: orbit (showcase) and fly (WASD exploration).
## Tab toggles between modes. Orbit mode auto-rotates around orbit_center.
## Fly mode: WASD move, Q/E elevation, right-click+drag mouse look, Shift sprint.

enum Mode { ORBIT, FLY, TRANSITIONING }

@export var orbit_speed: float = 20.0
@export var orbit_center: Vector3 = Vector3.ZERO
@export var fly_speed: float = 10.0
@export var sprint_multiplier: float = 2.0
@export var mouse_sensitivity: float = 0.002
@export var transition_speed: float = 3.0

var _mode: Mode = Mode.ORBIT
var _radius: float
var _orbit_height: float
var _orbit_angle: float

# Fly mode state
var _fly_yaw: float = 0.0
var _fly_pitch: float = 0.0
var _is_right_clicking: bool = false

# Transition state
var _transition_start_pos: Vector3
var _transition_start_basis: Basis
var _transition_progress: float = 0.0

func _ready() -> void:
	_orbit_height = position.y
	_radius = Vector2(position.x - orbit_center.x, position.z - orbit_center.z).length()
	_orbit_angle = atan2(position.z - orbit_center.z, position.x - orbit_center.x)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_TAB:
		_toggle_mode()
		get_viewport().set_input_as_handled()
	if _mode == Mode.FLY:
		if event is InputEventMouseButton:
			_is_right_clicking = event.pressed and event.button_index == MOUSE_BUTTON_RIGHT
		if event is InputEventMouseMotion and _is_right_clicking:
			_fly_yaw -= event.relative.x * mouse_sensitivity
			_fly_pitch -= event.relative.y * mouse_sensitivity
			_fly_pitch = clampf(_fly_pitch, -PI / 2.0 + 0.1, PI / 2.0 - 0.1)

func _process(delta: float) -> void:
	match _mode:
		Mode.ORBIT:
			_process_orbit(delta)
		Mode.FLY:
			_process_fly(delta)
		Mode.TRANSITIONING:
			_process_transition(delta)

func _toggle_mode() -> void:
	match _mode:
		Mode.ORBIT:
			# Orbit -> Fly: instant, capture current orientation
			_mode = Mode.FLY
			var euler := global_transform.basis.get_euler()
			_fly_yaw = euler.y
			_fly_pitch = euler.x
		Mode.FLY:
			# Fly -> Orbit: smooth transition back
			_mode = Mode.TRANSITIONING
			_transition_start_pos = global_position
			_transition_start_basis = global_transform.basis
			_transition_progress = 0.0
		Mode.TRANSITIONING:
			# During transition, Tab snaps to fly at current interpolated position
			_mode = Mode.FLY
			var euler := global_transform.basis.get_euler()
			_fly_yaw = euler.y
			_fly_pitch = euler.x

func _process_orbit(delta: float) -> void:
	_orbit_angle += deg_to_rad(orbit_speed) * delta
	position = Vector3(
		orbit_center.x + _radius * cos(_orbit_angle),
		_orbit_height,
		orbit_center.z + _radius * sin(_orbit_angle)
	)
	look_at(orbit_center, Vector3.UP)

func _process_fly(delta: float) -> void:
	var speed := fly_speed
	if Input.is_key_pressed(KEY_SHIFT):
		speed *= sprint_multiplier

	var input_dir := Vector3.ZERO
	if Input.is_key_pressed(KEY_W):
		input_dir.z -= 1.0
	if Input.is_key_pressed(KEY_S):
		input_dir.z += 1.0
	if Input.is_key_pressed(KEY_A):
		input_dir.x -= 1.0
	if Input.is_key_pressed(KEY_D):
		input_dir.x += 1.0
	if Input.is_key_pressed(KEY_Q):
		input_dir.y -= 1.0
	if Input.is_key_pressed(KEY_E):
		input_dir.y += 1.0

	# Build orientation from yaw/pitch
	var fly_basis := Basis.from_euler(Vector3(_fly_pitch, _fly_yaw, 0.0))
	global_transform.basis = fly_basis

	if input_dir.length_squared() > 0.0:
		input_dir = input_dir.normalized()
		global_position += fly_basis * input_dir * speed * delta

func _process_transition(delta: float) -> void:
	_transition_progress += delta * transition_speed
	var t := _ease_in_out(_transition_progress)

	if t >= 1.0:
		_mode = Mode.ORBIT
		# Sync orbit angle to where we ended up
		_orbit_angle = atan2(position.z - orbit_center.z, position.x - orbit_center.x)
		return

	# Compute current orbit target position
	var target_pos := Vector3(
		orbit_center.x + _radius * cos(_orbit_angle),
		_orbit_height,
		orbit_center.z + _radius * sin(_orbit_angle)
	)

	# Interpolate position
	global_position = _transition_start_pos.lerp(target_pos, t)

	# Interpolate orientation toward looking at orbit_center
	var target_transform := global_transform.looking_at(orbit_center, Vector3.UP)
	global_transform.basis = _transition_start_basis.slerp(target_transform.basis, t)

## Attempt a smooth ease-in/ease-out curve (smoothstep).
func _ease_in_out(t: float) -> float:
	var clamped := clampf(t, 0.0, 1.0)
	return clamped * clamped * (3.0 - 2.0 * clamped)
```

- [ ] **Step 2: Verify addon builds**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds (GDScript files don't affect C# build, but ensures no project corruption).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Add dual-mode fly/orbit camera controller

Tab toggles between orbit (auto-rotate showcase) and fly (WASD + mouse
exploration) modes. Fly->orbit transitions use smoothstep ease-in/out.
Tab during transition snaps back to fly at current interpolated position."
```

---

### Task 5: Create MainSceneWriter

**Files:**
- Create: `src/pmview-host-projector/src/PmviewHostProjector/Scaffolding/MainSceneWriter.cs`
- Create: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Scaffolding/MainSceneWriterTests.cs`

- [ ] **Step 1: Write failing tests for MainSceneWriter**

```csharp
using Xunit;
using PmviewHostProjector.Scaffolding;

namespace PmviewHostProjector.Tests.Scaffolding;

public class MainSceneWriterTests
{
    [Fact]
    public void Write_StartsWithGdSceneHeader()
    {
        var tscn = MainSceneWriter.Write();
        Assert.StartsWith("[gd_scene", tscn);
    }

    [Fact]
    public void Write_HasCameraWithFlyOrbitScript()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]", tscn);
        Assert.Contains("fly_orbit_camera.gd", tscn);
    }

    [Fact]
    public void Write_HasWorldEnvironment()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"WorldEnvironment\" type=\"WorldEnvironment\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_HasKeyLightAndFillLight()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"KeyLight\" type=\"DirectionalLight3D\" parent=\".\"]", tscn);
        Assert.Contains("[node name=\"FillLight\" type=\"DirectionalLight3D\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_HasSceneRootNode()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"SceneRoot\" type=\"Node3D\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_RootNodeIsNamedMain()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"Main\" type=\"Node3D\"]", tscn);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~MainSceneWriterTests"`
Expected: Build FAIL — `MainSceneWriter` does not exist.

- [ ] **Step 3: Implement MainSceneWriter**

```csharp
using System.Globalization;
using System.Text;
using PmviewHostProjector.Emission;

namespace PmviewHostProjector.Scaffolding;

/// <summary>
/// Emits main.tscn — the project's entry scene with camera, lighting,
/// environment, and a SceneRoot node for loading host-view scenes.
/// </summary>
public static class MainSceneWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Write()
    {
        var sb = new StringBuilder();

        // Header: 1 ext_resource (camera script) + 1 sub_resource (environment)
        sb.AppendLine("[gd_scene load_steps=2 format=3]");
        sb.AppendLine();

        sb.AppendLine("[ext_resource type=\"Script\" path=\"res://addons/pmview-bridge/building_blocks/fly_orbit_camera.gd\" id=\"fly_camera_script\"]");
        sb.AppendLine();

        // WorldEnvironment sub_resource
        sb.AppendLine("[sub_resource type=\"Environment\" id=\"world_env\"]");
        sb.AppendLine("background_mode = 1");
        sb.AppendLine("background_color = Color(0.02, 0.02, 0.06, 1)");
        sb.AppendLine("ambient_light_source = 1");
        sb.AppendLine("ambient_light_color = Color(0.4, 0.4, 0.5, 1)");
        sb.AppendLine("ambient_light_energy = 0.5");
        sb.AppendLine();

        // Root node
        sb.AppendLine("[node name=\"Main\" type=\"Node3D\"]");
        sb.AppendLine();

        // Camera
        sb.AppendLine("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]");
        sb.AppendLine("script = ExtResource(\"fly_camera_script\")");
        sb.AppendLine("transform = Transform3D(1, 0, 0, 0, 0.94, -0.34, 0, 0.34, 0.94, 0, 8, 15)");
        sb.AppendLine();

        // WorldEnvironment
        sb.AppendLine("[node name=\"WorldEnvironment\" type=\"WorldEnvironment\" parent=\".\"]");
        sb.AppendLine("environment = SubResource(\"world_env\")");
        sb.AppendLine();

        // Lights
        sb.AppendLine("[node name=\"KeyLight\" type=\"DirectionalLight3D\" parent=\".\"]");
        sb.AppendLine("transform = Transform3D(1, 0, 0, 0, 0.707, -0.707, 0, 0.707, 0.707, 0, 0, 0)");
        sb.AppendLine("light_energy = 1.2");
        sb.AppendLine("shadow_enabled = true");
        sb.AppendLine();

        sb.AppendLine("[node name=\"FillLight\" type=\"DirectionalLight3D\" parent=\".\"]");
        sb.AppendLine("transform = Transform3D(-1, 0, 0, 0, 0.866, 0.5, 0, 0.5, -0.866, 0, 0, 0)");
        sb.AppendLine("light_energy = 0.5");
        sb.AppendLine();

        // SceneRoot — host-view scenes loaded as children
        sb.AppendLine("[node name=\"SceneRoot\" type=\"Node3D\" parent=\".\"]");
        sb.AppendLine();

        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~MainSceneWriterTests"`
Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add MainSceneWriter — emits project main.tscn

Generates the entry scene with dual-mode camera, lighting rig,
WorldEnvironment, and SceneRoot node for hosting generated views."
```

---

### Task 6: Create ProjectScaffolder

**Files:**
- Create: `src/pmview-host-projector/src/PmviewHostProjector/Scaffolding/ProjectScaffolder.cs`
- Create: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Scaffolding/ProjectScaffolderTests.cs`

- [ ] **Step 1: Write failing tests for ProjectScaffolder**

```csharp
using Xunit;
using PmviewHostProjector.Scaffolding;

namespace PmviewHostProjector.Tests.Scaffolding;

public class ProjectScaffolderTests
{
    private string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pmview-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private void Cleanup(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    [Fact]
    public void Scaffold_CreatesProjectGodot()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            Assert.True(File.Exists(Path.Combine(dir, "project.godot")));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ProjectGodot_HasCorrectMainScene()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.Contains("run/main_scene=\"res://scenes/main.tscn\"", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ProjectGodot_HasCSharpFeature()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.Contains("C#", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_CreatesCsproj()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var csprojFiles = Directory.GetFiles(dir, "*.csproj");
            Assert.Single(csprojFiles);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_Csproj_TargetsNet8()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var csproj = Directory.GetFiles(dir, "*.csproj")[0];
            var content = File.ReadAllText(csproj);
            Assert.Contains("net8.0", content);
            Assert.Contains("Godot.NET.Sdk", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_CreatesSln()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var slnFiles = Directory.GetFiles(dir, "*.sln");
            Assert.Single(slnFiles);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_CreatesMainScene()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            Assert.True(File.Exists(Path.Combine(dir, "scenes", "main.tscn")));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ProjectNameDerivedFromDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"my-cool-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.Contains("my-cool-project", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_Idempotent_DoesNotClobberExistingProjectGodot()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var marker = "[custom_section]\nmy_key=true\n";
            File.AppendAllText(Path.Combine(dir, "project.godot"), marker);

            // Run again
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.Contains("my_key=true", content);
        }
        finally { Cleanup(dir); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~ProjectScaffolderTests"`
Expected: Build FAIL — `ProjectScaffolder` does not exist.

- [ ] **Step 3: Implement ProjectScaffolder**

```csharp
using System.Text;

namespace PmviewHostProjector.Scaffolding;

/// <summary>
/// Scaffolds a complete Godot .NET project: project.godot, .csproj, .sln,
/// and main.tscn with camera/lighting. Idempotent — safe to re-run.
/// </summary>
public static class ProjectScaffolder
{
    public static void Scaffold(string projectDir)
    {
        Directory.CreateDirectory(projectDir);
        var projectName = SanitiseProjectName(Path.GetFileName(Path.GetFullPath(projectDir)));

        WriteProjectGodotIfMissing(projectDir, projectName);
        WriteCsprojIfMissing(projectDir, projectName);
        WriteSlnIfMissing(projectDir, projectName);
        WriteMainScene(projectDir);
    }

    private static void WriteProjectGodotIfMissing(string dir, string name)
    {
        var path = Path.Combine(dir, "project.godot");
        if (File.Exists(path)) return;

        var content = $"""
            ; Engine configuration file (generated by pmview-host-projector)

            [application]

            config/name="{name}"
            config/description="PCP performance metrics as living 3D environments"
            run/main_scene="res://scenes/main.tscn"
            config/features=PackedStringArray("4.6", "C#", "Forward Plus")

            [dotnet]

            project/assembly_name="{name}"
            """;
        File.WriteAllText(path, content.Replace("            ", "") + "\n");
    }

    private static void WriteCsprojIfMissing(string dir, string name)
    {
        var path = Path.Combine(dir, $"{name}.csproj");
        if (Directory.GetFiles(dir, "*.csproj").Length > 0) return;

        var content = """
            <Project Sdk="Godot.NET.Sdk/4.6.1">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <EnableDynamicLoading>true</EnableDynamicLoading>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(path, content.Replace("            ", "") + "\n");
    }

    private static void WriteSlnIfMissing(string dir, string name)
    {
        if (Directory.GetFiles(dir, "*.sln").Length > 0) return;

        var guid = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var content = $"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}") = "{name}", "{name}.csproj", "{{{guid}}}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            		Release|Any CPU = Release|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{{{guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{{{guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU
            		{{{guid}}}.Release|Any CPU.Build.0 = Release|Any CPU
            	EndGlobalSection
            EndGlobal
            """;
        File.WriteAllText(Path.Combine(dir, $"{name}.sln"),
            content.Replace("            ", "") + "\n");
    }

    private static void WriteMainScene(string dir)
    {
        var scenesDir = Path.Combine(dir, "scenes");
        Directory.CreateDirectory(scenesDir);
        var path = Path.Combine(scenesDir, "main.tscn");
        File.WriteAllText(path, MainSceneWriter.Write());
    }

    private static string SanitiseProjectName(string dirName) =>
        string.IsNullOrWhiteSpace(dirName) ? "pmview-project" : dirName;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~ProjectScaffolderTests"`
Expected: All 9 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add ProjectScaffolder — generates complete Godot .NET project

Creates project.godot, .csproj, .sln, and main.tscn from a single
Scaffold(dir) call. Idempotent — skips existing project files,
always refreshes main.tscn."
```

---

### Task 7: Wire CLI subcommands into Program.cs

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Program.cs`

- [ ] **Step 1: Refactor Program.cs to support init subcommand and --init flag**

```csharp
using PcpClient;
using PmviewHostProjector.Discovery;
using PmviewHostProjector.Emission;
using PmviewHostProjector.Layout;
using PmviewHostProjector.Profiles;
using PmviewHostProjector.Scaffolding;

namespace PmviewHostProjector;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "init")
            return RunInit(args);

        return await RunGenerate(args);
    }

    private static int RunInit(string[] args)
    {
        var projectDir = args.Length > 1 ? Path.GetFullPath(args[1]) : Directory.GetCurrentDirectory();

        Console.WriteLine($"pmview init: scaffolding project in {projectDir}");

        try
        {
            ProjectScaffolder.Scaffold(projectDir);

            var addonSource = AddonInstaller.FindAddonSource();
            if (addonSource != null)
            {
                var repoRoot = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);
                if (repoRoot != null)
                {
                    AddonInstaller.InstallAddonWithLibraries(addonSource, projectDir, repoRoot);
                    Console.WriteLine("  Addon installed with libraries");
                }
            }

            Console.WriteLine($"  project.godot created");
            Console.WriteLine($"  scenes/main.tscn created");
            Console.WriteLine($"Project ready — open in Godot editor");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunGenerate(string[] args)
    {
        var pmproxyUrl = GetArg(args, "--pmproxy") ?? "http://localhost:44322";
        var outputPath = Path.GetFullPath(ResolveOutputPath(GetArg(args, "-o") ?? GetArg(args, "--output") ?? "host-view.tscn"));
        var shouldInit = HasFlag(args, "--init");
        var installAddon = HasFlag(args, "--install-addon");

        Console.WriteLine($"pmview-host-projector: connecting to {pmproxyUrl}");

        try
        {
            var godotRoot = AddonInstaller.FindGodotProjectRoot(outputPath);

            if (godotRoot == null && shouldInit)
            {
                // Derive project root from output path (parent of scenes/)
                var outputDir = Path.GetDirectoryName(outputPath)!;
                godotRoot = outputDir.EndsWith("scenes")
                    ? Path.GetDirectoryName(outputDir)!
                    : outputDir;

                Console.WriteLine($"No project.godot found — initialising project at {godotRoot}");
                ProjectScaffolder.Scaffold(godotRoot);

                var addonSource = AddonInstaller.FindAddonSource();
                if (addonSource != null)
                {
                    var repoRoot = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);
                    if (repoRoot != null)
                        AddonInstaller.InstallAddonWithLibraries(addonSource, godotRoot, repoRoot);
                }
            }
            else if (godotRoot == null)
            {
                Console.Error.WriteLine("Error: Cannot find Godot project root (no project.godot found).");
                Console.Error.WriteLine("Either run from inside a Godot project, or use --init to create one.");
                Console.Error.WriteLine("  pmview init <project-dir>");
                Console.Error.WriteLine("  pmview generate --init --pmproxy <url> -o <path>");
                return 1;
            }
            else if (installAddon)
            {
                var addonSource = AddonInstaller.FindAddonSource();
                if (addonSource == null)
                {
                    Console.Error.WriteLine("Error: Cannot find addon source. Run from the pmview-nextgen repository.");
                    return 1;
                }

                var repoRoot = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);
                if (repoRoot == null)
                {
                    Console.Error.WriteLine("Error: Cannot find repo root to build libraries.");
                    return 1;
                }

                var csprojPath = CsprojPatcher.FindTargetCsproj(godotRoot);
                if (csprojPath == null)
                {
                    Console.Error.WriteLine("Error: No .csproj found. Use --init instead of --install-addon for new projects.");
                    return 1;
                }

                AddonInstaller.InstallAddonWithLibraries(addonSource, godotRoot, repoRoot);
                Console.WriteLine($"Addon installed to: {Path.Combine(godotRoot, "addons", "pmview-bridge")}");
            }

            await using var pcpClient = new PcpClientConnection(new Uri(pmproxyUrl));
            await pcpClient.ConnectAsync();

            Console.WriteLine("Discovering host topology...");
            var topology = await MetricDiscovery.DiscoverAsync(pcpClient);
            Console.WriteLine($"  OS: {topology.Os}, Host: {topology.Hostname}");
            Console.WriteLine($"  CPUs: {topology.CpuInstances.Count}, " +
                              $"Disks: {topology.DiskDevices.Count}, " +
                              $"NICs: {topology.NetworkInterfaces.Count}");

            var zones = new HostProfileProvider().GetProfile(topology.Os);

            Console.WriteLine("Computing layout...");
            var layout = LayoutCalculator.Calculate(zones, topology);

            Console.WriteLine("Generating scene...");
            var tscn = SceneEmitter.Emit(layout, pmproxyUrl);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, tscn);
            Console.WriteLine($"Scene written to: {outputPath}");
            Console.WriteLine($"  {layout.Zones.Count} zones, " +
                              $"{layout.Zones.Sum(z => z.Shapes.Count)} shapes");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string? GetArg(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static bool HasFlag(string[] args, string flag) =>
        Array.IndexOf(args, flag) >= 0;

    private static string ResolveOutputPath(string path)
    {
        if (Directory.Exists(path) || !Path.HasExtension(path))
            return Path.Combine(path, "host-view.tscn");
        return path;
    }
}
```

- [ ] **Step 2: Run all tests**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All pass.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Add pmview init subcommand and --init flag to generate

pmview init <dir> scaffolds a complete Godot project from scratch.
pmview generate --init auto-scaffolds if no project.godot found.
Without --init, generate errors with helpful guidance."
```

---

### Task 8: Final verification and push

- [ ] **Step 1: Run full test suite**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests pass.

- [ ] **Step 2: Push and monitor CI**

```bash
git push
```

Launch background agent to monitor GitHub Actions.

- [ ] **Step 3: Update CLAUDE.md with new CLI commands**

Add the `pmview init` and `pmview generate --init` commands to the Commands section in CLAUDE.md.
