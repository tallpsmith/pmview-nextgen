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
		AssertThat(result.HasValue).IsTrue();
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
		AssertThat(result.HasValue).IsTrue();
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
		AssertThat(result.HasValue).IsTrue();
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
		AssertThat(result.HasValue).IsFalse();
	}

	[TestCase]
	[RequireGodotRuntime]
	public void ExtractValue_MissingInstanceId_ReturnsNull()
	{
		var binding = MakeBinding(instanceId: 999, instanceName: null);
		var instances = new Godot.Collections.Dictionary { [0] = 1.0 };
		var nameToId = new Godot.Collections.Dictionary();

		var result = SceneBinder.ExtractValue(binding, instances, nameToId);
		AssertThat(result.HasValue).IsFalse();
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
		await runner.AwaitIdleFrame();
	}

	// ── Initial value normalisation ─────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public async Task BindFromSceneProperties_InitialValueZero_NormalisesToTargetMin()
	{
		// Bug: InitialValue=0 passed raw to ApplyProperty → Scale.Y=0 →
		// degenerate basis. When RotateY runs (rotation_speed binding),
		// Godot can't recover the Y column and the node vanishes forever.
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var bindable = new PcpBindable();
		var heightBinding = new PcpBindingResource
		{
			MetricName = "test.metric",
			TargetProperty = "height",
			SourceRangeMin = 0f,
			SourceRangeMax = 3f,
			TargetRangeMin = 0.2f,
			TargetRangeMax = 25f,
			InitialValue = 0f
		};
		bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { heightBinding };
		node3D.AddChild(bindable);

		binder.BindFromSceneProperties(node3D);

		// InitialValue 0 (source range) should normalise to TargetRangeMin (0.2)
		AssertThat(node3D.Scale.Y).IsEqualApprox(0.2f, 0.01f);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task BindFromSceneProperties_HeightAndRotation_NodeRemainsVisible()
	{
		// Reproduces the disappearing cube: height + rotation_speed bindings
		// on same node. After initial values and a RotateY call, Scale.Y
		// must remain positive so the basis never degenerates.
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var bindable = new PcpBindable();
		var heightBinding = new PcpBindingResource
		{
			MetricName = "test.metric",
			TargetProperty = "height",
			SourceRangeMin = 0f,
			SourceRangeMax = 3f,
			TargetRangeMin = 0.2f,
			TargetRangeMax = 25f,
			InitialValue = 0f
		};
		var rotationBinding = new PcpBindingResource
		{
			MetricName = "test.metric",
			TargetProperty = "rotation_speed",
			SourceRangeMin = 0f,
			SourceRangeMax = 3f,
			TargetRangeMin = 0f,
			TargetRangeMax = 5400f,
			InitialValue = 0f
		};
		bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource>
			{ heightBinding, rotationBinding };
		node3D.AddChild(bindable);

		binder.BindFromSceneProperties(node3D);

		// Scale.Y must be positive (not 0) after initial binding
		AssertThat(node3D.Scale.Y).IsGreater(0f);

		// Simulate what _Process does — then verify transform survives
		node3D.RotateY(Mathf.DegToRad(12f));
		node3D.Scale = new Vector3(node3D.Scale.X, 3.5f, node3D.Scale.Z);

		// Scale.Y must survive the RotateY + Scale set cycle
		AssertThat(node3D.Scale.Y).IsEqualApprox(3.5f, 0.01f);
		await runner.AwaitIdleFrame();
	}

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

	// ── Smooth interpolation ─────────────────────────────────────────────

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

	// ── Text binding ───────────────────────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public void ApplyMetrics_TextBinding_SetsLabelText()
	{
		var binder = new SceneBinder();
		var label = new Label3D();
		label.Name = "TimestampLabel";
		binder.AddTextBindingForTest("pmview.meta.timestamp", label);

		var metrics = new Godot.Collections.Dictionary();
		metrics["pmview.meta.timestamp"] = new Godot.Collections.Dictionary
		{
			["text_value"] = "2025-03-14 · 14:23:07"
		};

		binder.ApplyMetrics(metrics);

		AssertThat(label.Text).IsEqual("2025-03-14 · 14:23:07");
	}

	[TestCase]
	[RequireGodotRuntime]
	public void ApplyMetrics_TextBinding_NoTextValueKey_DoesNotThrow()
	{
		var binder = new SceneBinder();
		var label = new Label3D();
		label.Name = "TimestampLabel";
		binder.AddTextBindingForTest("pmview.meta.timestamp", label);

		var metrics = new Godot.Collections.Dictionary();
		metrics["pmview.meta.timestamp"] = new Godot.Collections.Dictionary
		{
			["instances"] = new Godot.Collections.Dictionary()
		};

		binder.ApplyMetrics(metrics);   // must not throw
		AssertThat(label.Text).IsEqual("");
	}

	[TestCase]
	[RequireGodotRuntime]
	public void ApplyMetrics_TextBinding_MetricAbsent_DoesNotThrow()
	{
		var binder = new SceneBinder();
		var label = new Label3D();
		label.Name = "TimestampLabel";
		binder.AddTextBindingForTest("pmview.meta.timestamp", label);

		binder.ApplyMetrics(new Godot.Collections.Dictionary());  // empty dict
		AssertThat(label.Text).IsEqual("");
	}

	// ── MetricPoller virtual metrics ───────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public void InjectVirtualMetrics_IncludesTimestampTextValue()
	{
		var poller = new MetricPoller();
		var dict = new Godot.Collections.Dictionary();

		poller.InjectVirtualMetrics(dict);

		AssertThat(dict.ContainsKey("pmview.meta.timestamp")).IsTrue();
		var entry = dict["pmview.meta.timestamp"].AsGodotDictionary();
		AssertThat(entry.ContainsKey("text_value")).IsTrue();
		AssertThat(entry["text_value"].AsString()).IsNotEmpty();
	}

	[TestCase]
	[RequireGodotRuntime]
	public void InjectVirtualMetrics_IncludesHostname_WhenSet()
	{
		var poller = new MetricPoller();
		poller.Hostname = "my-server";
		var dict = new Godot.Collections.Dictionary();

		poller.InjectVirtualMetrics(dict);

		AssertThat(dict.ContainsKey("pmview.meta.hostname")).IsTrue();
		var entry = dict["pmview.meta.hostname"].AsGodotDictionary();
		AssertThat(entry["text_value"].AsString()).IsEqual("my-server");
	}

	[TestCase]
	[RequireGodotRuntime]
	public void InjectVirtualMetrics_OmitsHostname_WhenEmpty()
	{
		var poller = new MetricPoller();   // Hostname = "" by default
		var dict = new Godot.Collections.Dictionary();

		poller.InjectVirtualMetrics(dict);

		AssertThat(dict.ContainsKey("pmview.meta.hostname")).IsFalse();
	}

	// ── IsBound property ────────────────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public void IsBound_FalseBeforeBinding()
	{
		var binder = new SceneBinder();
		AssertThat(binder.IsBound).IsFalse();
	}

	// ── Range tuning ────────────────────────────────────────────────────

	[TestCase]
	public void Normalise_RangeChangeAffectsOutput()
	{
		// Same raw value normalises to a smaller target when the range widens.
		var beforeUpdate = SceneBinder.Normalise(250_000_000, 0, 500_000_000, 0.2, 5.0);
		var afterUpdate = SceneBinder.Normalise(250_000_000, 0, 1_000_000_000, 0.2, 5.0);

		AssertThat(beforeUpdate).IsEqual(2.6);   // 50% of range → midpoint of 0.2–5.0
		AssertThat(afterUpdate).IsEqual(1.4);    // 25% of range → quarter of 0.2–5.0
		AssertThat(afterUpdate).IsLess(beforeUpdate);
	}

	[TestCase]
	[RequireGodotRuntime]
	public void UpdateSourceRangeMax_CanBeCalled()
	{
		// Verifies the public API exists. Calling on an empty binder is a no-op.
		var binder = new SceneBinder();
		binder.UpdateSourceRangeMax("Disk", 1_000_000_000.0);
		// No exception = method exists and handles empty binding list gracefully.
	}

	[TestCase]
	[RequireGodotRuntime]
	public void GetZoneCentroid_CanBeCalled()
	{
		var binder = new SceneBinder();
		var centroid = binder.GetZoneCentroid("Disk");
		AssertThat(centroid).IsEqual(Godot.Vector3.Zero);
	}

	// ── GetBindingsForNode ───────────────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public async Task GetBindingsForNode_ReturnsBindingsGroupedByProperty()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var bindable = new PcpBindable();
		var heightBinding = new PcpBindingResource
		{
			MetricName = "kernel.cpu.user",
			TargetProperty = "height",
			SourceRangeMin = 0f, SourceRangeMax = 100f,
			TargetRangeMin = 0.2f, TargetRangeMax = 5.0f,
			InitialValue = 0f,
			ZoneName = "CPU",
			InstanceName = "cpu0"
		};
		bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { heightBinding };
		node3D.AddChild(bindable);
		binder.BindFromSceneProperties(node3D);

		var metrics = new Godot.Collections.Dictionary
		{
			["kernel.cpu.user"] = new Godot.Collections.Dictionary
			{
				["instances"] = new Godot.Collections.Dictionary { [7] = 42.3 },
				["name_to_id"] = new Godot.Collections.Dictionary { ["cpu0"] = 7 }
			}
		};
		binder.ApplyMetrics(metrics);

		var result = binder.GetBindingsForNode(node3D);
		AssertThat(result.ContainsKey("zone")).IsTrue();
		AssertThat(result["zone"].AsString()).IsEqual("CPU");
		AssertThat(result.ContainsKey("instance")).IsTrue();
		AssertThat(result["instance"].AsString()).IsEqual("cpu0");
		AssertThat(result.ContainsKey("properties")).IsTrue();

		var properties = result["properties"].AsGodotDictionary();
		AssertThat(properties.ContainsKey("height")).IsTrue();

		var heightBindings = properties["height"].AsGodotArray();
		AssertThat(heightBindings.Count).IsEqual(1);

		var entry = heightBindings[0].AsGodotDictionary();
		AssertThat(entry["metric"].AsString()).IsEqual("kernel.cpu.user");
		AssertThat(entry["value"].AsDouble()).IsEqualApprox(42.3, 0.01);

		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public void GetBindingsForNode_NoBindings_ReturnsEmptyDict()
	{
		var binder = new SceneBinder();
		var node = new Node3D();
		var result = binder.GetBindingsForNode(node);
		AssertThat(result.Count).IsEqual(0);
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task GetBindingsForNode_NoMetricsApplied_ReturnsNullValue()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var bindable = new PcpBindable();
		var binding = new PcpBindingResource
		{
			MetricName = "kernel.cpu.user",
			TargetProperty = "height",
			SourceRangeMin = 0f, SourceRangeMax = 100f,
			TargetRangeMin = 0.2f, TargetRangeMax = 5.0f,
			InitialValue = 0f,
			ZoneName = "CPU",
			InstanceName = "cpu0"
		};
		bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { binding };
		node3D.AddChild(bindable);
		binder.BindFromSceneProperties(node3D);

		var result = binder.GetBindingsForNode(node3D);
		var properties = result["properties"].AsGodotDictionary();
		var heightEntries = properties["height"].AsGodotArray();
		var entry = heightEntries[0].AsGodotDictionary();

		AssertThat(entry["value"].VariantType).IsEqual(Variant.Type.Nil);

		await runner.AwaitIdleFrame();
	}

	// ── UpdateSourceRangeMax ─────────────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public async Task UpdateSourceRangeMax_AnimationContinuesAfterRangeChange()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var node3D = (Node3D)runner.Scene();
		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		var bindable = new PcpBindable();
		var binding = new PcpBindingResource
		{
			MetricName = "disk.dev.read_bytes",
			TargetProperty = "height",
			SourceRangeMin = 0f, SourceRangeMax = 500_000_000f, // 500 MB/s
			TargetRangeMin = 0.2f, TargetRangeMax = 5.0f,
			InitialValue = 0f,
			ZoneName = "Disk"
		};
		bindable.PcpBindings = new Godot.Collections.Array<PcpBindingResource> { binding };
		node3D.AddChild(bindable);
		binder.BindFromSceneProperties(node3D);

		// First update: establish a baseline value (250 MB/s = midpoint)
		binder.ApplyMetrics(MakeSingularMetrics("disk.dev.read_bytes", 250_000_000.0));
		binder.AdvanceInterpolations(0.016f);
		var baselineY = node3D.Scale.Y;
		AssertThat(baselineY).IsGreater(2.0f); // should be around 2.6

		// Change the source range (simulates F1 tuner: 500 MB/s → 14 GB/s)
		binder.UpdateSourceRangeMax("Disk", 14_000_000_000.0);

		// Apply metrics with a NEW value after range change
		binder.ApplyMetrics(MakeSingularMetrics("disk.dev.read_bytes", 1_000_000_000.0));

		// Advance several frames — value must move toward the new target
		for (int i = 0; i < 10; i++)
			binder.AdvanceInterpolations(0.1f);

		var afterTunerY = node3D.Scale.Y;
		// The value should have changed — animation must not be frozen
		AssertThat(afterTunerY).IsNotEqual(baselineY);

		await runner.AwaitIdleFrame();
	}

	// ── Helpers ──────────────────────────────────────────────────────────

	private static Godot.Collections.Dictionary MakeSingularMetrics(
		string metricName, double value)
	{
		return new Godot.Collections.Dictionary
		{
			[metricName] = new Godot.Collections.Dictionary
			{
				["instances"] = new Godot.Collections.Dictionary { [-1] = value },
				["name_to_id"] = new Godot.Collections.Dictionary()
			}
		};
	}

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
