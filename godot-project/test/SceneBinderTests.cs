using Godot;
using GdUnit4;
using PcpGodotBridge;
using PmviewNextgen.Bridge;
using static GdUnit4.Assertions;

namespace PmviewNextgen.Tests;

/// <summary>
/// Tests for SceneBinder: normalisation (pure math), value extraction
/// (dictionary lookup), property application, and binding validation.
/// </summary>
[TestSuite]
public partial class SceneBinderTests
{
	// ── Normalisation (pure math, no Godot runtime needed) ─────────────

	[TestCase]
	public void Normalise_LinearMapping_MidpointMapsCorrectly()
	{
		var result = SceneBinder.Normalise(50, 0, 100, 0, 1);
		AssertThat(result).IsEqual(0.5);
	}

	[TestCase]
	public void Normalise_BelowSourceMin_ClampsToTargetMin()
	{
		var result = SceneBinder.Normalise(-10, 0, 100, 0, 1);
		AssertThat(result).IsEqual(0.0);
	}

	[TestCase]
	public void Normalise_AboveSourceMax_ClampsToTargetMax()
	{
		var result = SceneBinder.Normalise(150, 0, 100, 0, 1);
		AssertThat(result).IsEqual(1.0);
	}

	[TestCase]
	public void Normalise_ZeroWidthSourceRange_ReturnsTargetMin()
	{
		var result = SceneBinder.Normalise(50, 50, 50, 0, 1);
		AssertThat(result).IsEqual(0.0);
	}

	[TestCase]
	public void Normalise_InvertedTargetRange_InterpolatesCorrectly()
	{
		// tgtMin > tgtMax — linear interpolation should still work
		var result = SceneBinder.Normalise(50, 0, 100, 1, 0);
		AssertThat(result).IsEqual(0.5);
	}

	[TestCase]
	public void Normalise_SourceMinEqualsValue_ReturnsTargetMin()
	{
		var result = SceneBinder.Normalise(0, 0, 100, 2, 8);
		AssertThat(result).IsEqual(2.0);
	}

	[TestCase]
	public void Normalise_SourceMaxEqualsValue_ReturnsTargetMax()
	{
		var result = SceneBinder.Normalise(100, 0, 100, 2, 8);
		AssertThat(result).IsEqual(8.0);
	}

	[TestCase]
	public void Normalise_NegativeSourceRange_MapsCorrectly()
	{
		// src [-50, 50] → tgt [0, 1], value 0 → 0.5
		var result = SceneBinder.Normalise(0, -50, 50, 0, 1);
		AssertThat(result).IsEqual(0.5);
	}

	// ── Value extraction (uses Godot Dictionaries) ────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public void ExtractValue_SingularMetric_ReadsKeyMinusOne()
	{
		var binding = MakeBinding(instanceId: null, instanceName: null);
		var instances = new Godot.Collections.Dictionary { [-1] = 42.0 };
		var nameToId = new Godot.Collections.Dictionary();

		var result = SceneBinder.ExtractValue(binding, instances, nameToId);
		AssertThat(result).IsNotNull();
		AssertThat(result!.Value).IsEqual(42.0);
	}

	[TestCase]
	[RequireGodotRuntime]
	public void ExtractValue_ByInstanceId_ReadsCorrectKey()
	{
		var binding = MakeBinding(instanceId: 3, instanceName: null);
		var instances = new Godot.Collections.Dictionary { [3] = 99.0, [5] = 11.0 };
		var nameToId = new Godot.Collections.Dictionary();

		var result = SceneBinder.ExtractValue(binding, instances, nameToId);
		AssertThat(result).IsNotNull();
		AssertThat(result!.Value).IsEqual(99.0);
	}

	[TestCase]
	[RequireGodotRuntime]
	public void ExtractValue_ByInstanceName_ResolvesViaNameToId()
	{
		var binding = MakeBinding(instanceId: null, instanceName: "cpu0");
		var instances = new Godot.Collections.Dictionary { [7] = 77.0 };
		var nameToId = new Godot.Collections.Dictionary { ["cpu0"] = 7 };

		var result = SceneBinder.ExtractValue(binding, instances, nameToId);
		AssertThat(result).IsNotNull();
		AssertThat(result!.Value).IsEqual(77.0);
	}

	[TestCase]
	[RequireGodotRuntime]
	public void ExtractValue_MissingInstanceName_ReturnsNull()
	{
		var binding = MakeBinding(instanceId: null, instanceName: "cpu99");
		var instances = new Godot.Collections.Dictionary { [0] = 1.0 };
		var nameToId = new Godot.Collections.Dictionary { ["cpu0"] = 0 };

		var result = SceneBinder.ExtractValue(binding, instances, nameToId);
		AssertThat(result).IsNull();
	}

	[TestCase]
	[RequireGodotRuntime]
	public void ExtractValue_MissingInstanceId_ReturnsNull()
	{
		var binding = MakeBinding(instanceId: 999, instanceName: null);
		var instances = new Godot.Collections.Dictionary { [0] = 1.0 };
		var nameToId = new Godot.Collections.Dictionary();

		var result = SceneBinder.ExtractValue(binding, instances, nameToId);
		AssertThat(result).IsNull();
	}

	[TestCase]
	[RequireGodotRuntime]
	public void ExtractValue_NoInstances_ReturnsNull()
	{
		var binding = MakeBinding(instanceId: null, instanceName: null);
		var instances = new Godot.Collections.Dictionary();
		var nameToId = new Godot.Collections.Dictionary();

		var result = SceneBinder.ExtractValue(binding, instances, nameToId);
		AssertThat(result).IsNull();
	}

	// ── Property application ([RequireGodotRuntime], scene runner) ─────

	[TestCase]
	[RequireGodotRuntime]
	public async Task ApplyBuiltIn_Height_SetsScaleY()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var binding = MakeResolvedBinding("height", PropertyKind.BuiltIn, "scale:y");
		binder.ApplyBuiltInProperty(node3D, binding.Binding.Property, 3.5f);

		AssertThat(node3D.Scale.Y).IsEqual(3.5f);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task ApplyBuiltIn_Width_SetsScaleX()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var binding = MakeResolvedBinding("width", PropertyKind.BuiltIn, "scale:x");
		binder.ApplyBuiltInProperty(node3D, binding.Binding.Property, 2.0f);

		AssertThat(node3D.Scale.X).IsEqual(2.0f);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task ApplyBuiltIn_Scale_SetsAllAxes()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var binding = MakeResolvedBinding("scale", PropertyKind.BuiltIn, "scale");
		binder.ApplyBuiltInProperty(node3D, binding.Binding.Property, 4.0f);

		AssertThat(node3D.Scale.X).IsEqual(4.0f);
		AssertThat(node3D.Scale.Y).IsEqual(4.0f);
		AssertThat(node3D.Scale.Z).IsEqual(4.0f);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task ApplyBuiltIn_PositionY_SetsYPosition()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var binding = MakeResolvedBinding("position_y", PropertyKind.BuiltIn, "position:y");
		binder.ApplyBuiltInProperty(node3D, binding.Binding.Property, 5.5f);

		AssertThat(node3D.Position.Y).IsEqual(5.5f);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task ApplyBuiltIn_ColorTemperature_SetsHue()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_mesh_with_material.tscn");
		var mesh = (MeshInstance3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var binding = MakeResolvedBinding("color_temperature", PropertyKind.BuiltIn, "color_temperature");
		binder.ApplyBuiltInProperty(mesh, binding.Binding.Property, 1.0f); // hot = red (hue 0)

		var mat = (StandardMaterial3D)mesh.MaterialOverride!;
		// Hue should be near 0 (red) for value 1.0
		AssertThat(mat.AlbedoColor.H).IsLess(0.05f);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task ApplyBuiltIn_Opacity_SetsAlpha()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_mesh_with_material.tscn");
		var mesh = (MeshInstance3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var binding = MakeResolvedBinding("opacity", PropertyKind.BuiltIn, "opacity");
		binder.ApplyBuiltInProperty(mesh, binding.Binding.Property, 0.3f);

		var mat = (StandardMaterial3D)mesh.MaterialOverride!;
		AssertThat(mat.AlbedoColor.A).IsEqualApprox(0.3f, 0.01f);
		await runner.AwaitIdleFrame();
	}

	// ── Scene lifecycle ──────────────────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public async Task UnloadCurrentScene_ClearsAllState()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		binder.UnloadCurrentScene();

		AssertThat(binder.ActiveBindingCount).IsEqual(0);
		AssertThat(binder.CurrentConfig).IsNull();
		await runner.AwaitIdleFrame();
	}

	// ── Helpers ──────────────────────────────────────────────────────────

	private static MetricBinding MakeBinding(int? instanceId, string? instanceName)
	{
		return new MetricBinding(
			"TestNode", "test.metric", "height",
			0, 100, 0, 1,
			instanceId, instanceName);
	}

	private static ResolvedBinding MakeResolvedBinding(
		string property, PropertyKind kind, string godotProperty)
	{
		var binding = new MetricBinding(
			"TestNode", "test.metric", property,
			0, 100, 0, 1, null, null);
		return new ResolvedBinding(binding, kind, godotProperty);
	}

}
