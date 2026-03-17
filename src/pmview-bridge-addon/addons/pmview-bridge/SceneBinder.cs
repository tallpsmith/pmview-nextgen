using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PcpGodotBridge;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Discovers PcpBindable nodes in scenes, resolves bindings, and applies
/// metric values to scene node properties at runtime.
/// Validates properties against real nodes at scene load time.
/// </summary>
public partial class SceneBinder : Node
{
	[Signal]
	public delegate void BindingErrorEventHandler(string message);

	[Signal]
	public delegate void BindingsReadyEventHandler();

	public bool IsBound { get; private set; }

	/// <summary>
	/// Controls how quickly displayed values converge to polled targets.
	/// Higher = faster. Uses frame-rate-independent exponential decay.
	/// Default 5 gives a smooth ~0.2s response.
	/// </summary>
	[Export] public float SmoothSpeed { get; set; } = 5.0f;

	private Node? _currentScene;
	private readonly List<ActiveBinding> _activeBindings = new();
	private readonly Dictionary<Node3D, float> _rotationSpeeds = new();
	private readonly Dictionary<ActiveBinding, (float Current, float Target)> _smoothValues = new();

	/// <summary>
	/// A validated, resolved binding with a cached node reference.
	/// Only created for bindings that passed both config and scene validation.
	/// </summary>
	private record ActiveBinding(
		ResolvedBinding Resolved,
		Node TargetNode);

	public int ActiveBindingCount => _activeBindings.Count;
	private readonly Dictionary<string, List<Node3D>> _instanceNodes = new();

	public override void _Process(double delta)
	{
		AdvanceAnimations((float)delta);
	}

	/// <summary>
	/// Advances all animation state by the given time delta.
	/// Called by _Process; also callable directly from tests for deterministic control.
	/// </summary>
	internal void AdvanceAnimations(float delta)
	{
		AdvanceRotations(delta);
		AdvanceInterpolations(delta);
	}

	/// <summary>
	/// Applies delta-scaled rotation to all nodes with active rotation_speed bindings.
	/// </summary>
	internal void AdvanceRotations(float delta)
	{
		foreach (var (node, degreesPerSecond) in _rotationSpeeds)
		{
			if (IsInstanceValid(node))
				node.RotateY(Mathf.DegToRad(degreesPerSecond) * delta);
		}
	}

	/// <summary>
	/// Advances exponential-decay interpolation toward smooth targets
	/// and applies the updated values to node properties.
	/// </summary>
	internal void AdvanceInterpolations(float delta)
	{
		var smoothFactor = 1f - MathF.Exp(-delta * SmoothSpeed);
		foreach (var key in _smoothValues.Keys.ToList())
		{
			if (!IsInstanceValid(key.TargetNode))
			{
				_smoothValues.Remove(key);
				continue;
			}
			var (current, target) = _smoothValues[key];
			var next = Mathf.Lerp(current, target, smoothFactor);
			_smoothValues[key] = (next, target);
			ApplyBuiltInProperty(key.TargetNode, key.Resolved.Binding.Property, next);
		}
	}

	/// <summary>
	/// Discovers PcpBindable nodes in the scene tree, reads their binding resources,
	/// resolves and validates against real nodes, applies initial values.
	/// Returns distinct metric names needed for polling.
	/// </summary>
	public string[] BindFromSceneProperties(Node sceneRoot)
	{
		_activeBindings.Clear();
		_rotationSpeeds.Clear();
		_smoothValues.Clear();
		_currentScene = sceneRoot;

		var metricNames = new HashSet<string>();
		var bindableNodes = new List<PcpBindable>();

		FindBindableNodes(sceneRoot, bindableNodes);

		foreach (var bindable in bindableNodes)
		{
			var ownerNode = bindable.GetParent();
			if (ownerNode == null) continue;

			foreach (var bindingResource in bindable.PcpBindings)
			{
				if (bindingResource == null) continue;

				var metricBinding = bindingResource.ToMetricBinding(ownerNode.Name);
				var resolved = PropertyVocabulary.Resolve(metricBinding);

				if (!ValidatePropertyExists(ownerNode, resolved))
					continue;

				_activeBindings.Add(new ActiveBinding(resolved, ownerNode));
				metricNames.Add(metricBinding.Metric);

				// Text bindings have no numeric initial value — skip normalise/apply.
				if (metricBinding.Property == "text")
					continue;

				var normalisedInitial = Normalise(metricBinding.InitialValue,
					metricBinding.SourceRangeMin, metricBinding.SourceRangeMax,
					metricBinding.TargetRangeMin, metricBinding.TargetRangeMax);
				ApplyProperty(new ActiveBinding(resolved, ownerNode), (float)normalisedInitial);

				GD.Print($"[SceneBinder] Bound from scene: {ownerNode.Name}.{metricBinding.Property} <- {metricBinding.Metric}");
			}
		}

		GD.Print($"[SceneBinder] {_activeBindings.Count} bindings from scene properties");
		IsBound = true;
		EmitSignal(SignalName.BindingsReady);
		return metricNames.ToArray();
	}

	/// <summary>
	/// Apply metric values from a MetricPoller signal to the active bindings.
	/// Called every poll cycle. Hot path — no validation, just cached lookups.
	/// </summary>
	public void ApplyMetrics(Godot.Collections.Dictionary metrics)
	{
		foreach (var active in _activeBindings)
		{
			var binding = active.Resolved.Binding;

			if (!metrics.ContainsKey(binding.Metric))
				continue;

			// Text bindings carry a string value — handled separately, no normalisation.
			if (binding.Property == "text")
			{
				ApplyTextMetric(active, metrics);
				continue;
			}

			var metricData = metrics[binding.Metric].AsGodotDictionary();
			if (!metricData.ContainsKey("instances"))
				continue;
			var instances = metricData["instances"].AsGodotDictionary();
			var nameToId = metricData.ContainsKey("name_to_id")
				? metricData["name_to_id"].AsGodotDictionary()
				: new Godot.Collections.Dictionary();

			double? rawValue = ExtractValue(binding, instances, nameToId);
			if (rawValue == null)
			{
				var instanceLabel = binding.InstanceName != null
					? $"name='{binding.InstanceName}'"
					: $"id={binding.InstanceId}";
				GD.Print($"[SceneBinder] {binding.SceneNode}: no value for " +
					$"{binding.Metric}[{instanceLabel}] " +
					$"(available keys: {string.Join(",", instances.Keys)})");
				continue;
			}

			var normalised = Normalise(rawValue.Value,
				binding.SourceRangeMin, binding.SourceRangeMax,
				binding.TargetRangeMin, binding.TargetRangeMax);

			GD.Print($"[SceneBinder] {binding.SceneNode}.{binding.Property}: " +
				$"raw={rawValue.Value:F4} -> normalised={normalised:F4} " +
				$"(src [{binding.SourceRangeMin}-{binding.SourceRangeMax}] " +
				$"-> tgt [{binding.TargetRangeMin}-{binding.TargetRangeMax}])");

			if (IsSmoothable(active))
				SetSmoothTarget(active, (float)normalised);
			else
				ApplyProperty(active, (float)normalised);
		}
	}

	/// <summary>
	/// Updates SourceRangeMax for all active bindings matching the given zone.
	/// Stub — full implementation provided by Task 7.
	/// </summary>
	public void UpdateSourceRangeMax(string zoneName, double newMax)
	{
		// Task 7 implements the body; stub present for compilation.
	}

	/// <summary>
	/// Returns {zoneName: currentSourceRangeMax} for each zone with active bindings.
	/// Only returns the SourceRangeMax from bytes-throughput bindings.
	/// Zones with no active bindings are omitted.
	/// </summary>
	public Godot.Collections.Dictionary GetSourceRanges()
	{
		var result = new Godot.Collections.Dictionary();
		foreach (var active in _activeBindings)
		{
			var binding = active.Resolved.Binding;
			if (binding.ZoneName == null) continue;
			if (!binding.Metric.Contains("bytes")) continue;
			if (result.ContainsKey(binding.ZoneName)) continue;

			result[binding.ZoneName] = binding.SourceRangeMax;
		}
		return result;
	}

	/// <summary>
	/// Creates distinct 3D objects for each instance of a metric.
	/// Clones a template node and creates one binding per instance.
	/// Call after scene is loaded and instance domain is known.
	/// </summary>
	public void CreatePerInstanceBindings(string metricName, string templateNodePath,
		string property, double sourceMin, double sourceMax,
		double targetMin, double targetMax,
		Godot.Collections.Array instances)
	{
		if (_currentScene == null)
			return;

		var templateNode = _currentScene.GetNodeOrNull<Node3D>(templateNodePath);
		if (templateNode == null)
		{
			GD.PushWarning(
				$"[SceneBinder] Template node not found: '{templateNodePath}'");
			return;
		}

		ClearInstanceNodes(metricName);

		var createdNodes = new List<Node3D>();
		var spacing = 2.0f;

		for (int i = 0; i < instances.Count; i++)
		{
			var instDict = instances[i].AsGodotDictionary();
			var instanceId = instDict["id"].AsInt32();
			var instanceName = instDict["name"].AsString();

			var clone = (Node3D)templateNode.Duplicate();
			clone.Name = $"{templateNode.Name}_{instanceName}";
			clone.Position = templateNode.Position + new Vector3(spacing * i, 0, 0);
			templateNode.GetParent().AddChild(clone);

			var binding = new PcpGodotBridge.MetricBinding(
				clone.Name, metricName, property,
				sourceMin, sourceMax, targetMin, targetMax,
				InstanceId: instanceId, InstanceName: null);

			var resolved = PcpGodotBridge.PropertyVocabulary.Resolve(binding);
			_activeBindings.Add(new ActiveBinding(resolved, clone));
			createdNodes.Add(clone);

			GD.Print($"[SceneBinder] Instance binding: {clone.Name}.{property} " +
					 $"<- {metricName}[{instanceName}]");
		}

		_instanceNodes[metricName] = createdNodes;
		templateNode.Visible = false;

		GD.Print($"[SceneBinder] Created {createdNodes.Count} per-instance nodes " +
				 $"for {metricName}");
	}

	public void UnloadCurrentScene()
	{
		IsBound = false;
		_activeBindings.Clear();
		_rotationSpeeds.Clear();
		_smoothValues.Clear();

		foreach (var nodes in _instanceNodes.Values)
		{
			foreach (var node in nodes)
			{
				if (IsInstanceValid(node))
					node.QueueFree();
			}
		}
		_instanceNodes.Clear();

		if (_currentScene != null)
		{
			_currentScene.QueueFree();
			_currentScene = null;
		}
	}

	private static void FindBindableNodes(Node root, List<PcpBindable> results)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is PcpBindable bindable)
				results.Add(bindable);

			FindBindableNodes(child, results);
		}
	}

	private void ClearInstanceNodes(string metricName)
	{
		if (!_instanceNodes.TryGetValue(metricName, out var nodes))
			return;

		_activeBindings.RemoveAll(ab => nodes.Contains(ab.TargetNode));

		foreach (var node in nodes)
		{
			if (IsInstanceValid(node))
			{
				_rotationSpeeds.Remove(node);
				node.QueueFree();
			}
		}

		_instanceNodes.Remove(metricName);
	}

	private bool ValidatePropertyExists(Node node, ResolvedBinding resolved)
	{
		var godotProperty = resolved.GodotPropertyName;

		var propertyName = godotProperty.Contains(':')
			? godotProperty.Split(':')[0]
			: godotProperty;

		var propertyList = node.GetPropertyList();
		foreach (var prop in propertyList)
		{
			if (prop["name"].AsString() == propertyName)
				return true;
		}

		var available = new List<string>();
		foreach (var prop in propertyList)
		{
			var name = prop["name"].AsString();
			var usage = prop["usage"].AsInt32();
			if ((usage & (int)PropertyUsageFlags.ScriptVariable) != 0)
				available.Add(name);
		}

		var availableStr = available.Count > 0
			? $" Available script properties: {string.Join(", ", available)}"
			: " No script properties found on this node.";

		GD.PushWarning(
			$"[SceneBinder] Property '{resolved.GodotPropertyName}' not found on " +
			$"node '{resolved.Binding.SceneNode}'.{availableStr}");
		EmitSignal(SignalName.BindingError,
			$"Property '{resolved.GodotPropertyName}' not found on " +
			$"'{resolved.Binding.SceneNode}'");

		return false;
	}

	internal static double? ExtractValue(MetricBinding binding,
		Godot.Collections.Dictionary instances,
		Godot.Collections.Dictionary nameToId)
	{
		if (binding.InstanceName != null)
		{
			if (!nameToId.ContainsKey(binding.InstanceName))
			{
				GD.Print($"[SceneBinder] {binding.SceneNode}: instance name " +
					$"'{binding.InstanceName}' not found for {binding.Metric} " +
					$"(available: {string.Join(", ", nameToId.Keys)})");
				return null;
			}
			var resolvedId = nameToId[binding.InstanceName].AsInt32();
			return instances.ContainsKey(resolvedId)
				? instances[resolvedId].AsDouble()
				: null;
		}

		if (binding.InstanceId != null)
		{
			return instances.ContainsKey(binding.InstanceId.Value)
				? instances[binding.InstanceId.Value].AsDouble()
				: null;
		}

		if (instances.ContainsKey(-1))
			return instances[-1].AsDouble();

		foreach (var key in instances.Keys)
			return instances[key].AsDouble();

		return null;
	}

	/// <summary>
	/// Smoothable: built-in scalar properties. rotation_speed is excluded because
	/// it drives continuous delta rotation in _Process, not a target position.
	/// Custom properties are left as immediate since their type is unknown.
	/// </summary>
	private static bool IsSmoothable(ActiveBinding active) =>
		active.Resolved.Kind == PropertyKind.BuiltIn &&
		active.Resolved.Binding.Property != "rotation_speed";

	/// <summary>
	/// On first call, snaps current to target (avoids lerping from 0 on scene load).
	/// On subsequent calls, updates only the target; current position is preserved
	/// so the next _Process frame continues from wherever the interpolation left off.
	/// </summary>
	private void SetSmoothTarget(ActiveBinding active, float target)
	{
		if (!_smoothValues.TryGetValue(active, out var existing))
			_smoothValues[active] = (target, target);
		else
			_smoothValues[active] = (existing.Current, target);
	}

	private void ApplyProperty(ActiveBinding active, float value)
	{
		switch (active.Resolved.Kind)
		{
			case PropertyKind.BuiltIn:
				ApplyBuiltInProperty(active.TargetNode, active.Resolved.Binding.Property, value);
				break;
			case PropertyKind.Custom:
				active.TargetNode.Set(active.Resolved.GodotPropertyName, value);
				break;
		}
	}

	internal void ApplyBuiltInProperty(Node node, string property, float value)
	{
		if (node is not Node3D node3D)
		{
			GD.PushWarning($"[SceneBinder] Built-in property '{property}' requires " +
						   $"Node3D but got {node.GetClass()}");
			return;
		}

		switch (property)
		{
			case "height":
				node3D.Scale = new Vector3(node3D.Scale.X, value, node3D.Scale.Z);
				break;
			case "width":
				node3D.Scale = new Vector3(value, node3D.Scale.Y, node3D.Scale.Z);
				break;
			case "depth":
				node3D.Scale = new Vector3(node3D.Scale.X, node3D.Scale.Y, value);
				break;
			case "scale":
				node3D.Scale = new Vector3(value, value, value);
				break;
			case "rotation_speed":
				_rotationSpeeds[node3D] = value;
				break;
			case "position_y":
				node3D.Position = new Vector3(node3D.Position.X, value, node3D.Position.Z);
				break;
			case "color_temperature":
				ApplyColorTemperature(node3D, value);
				break;
			case "opacity":
				ApplyOpacity(node3D, value);
				break;
		}
	}

	private static void ApplyColorTemperature(Node3D node, float value)
	{
		if (node is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D mat)
		{
			var hue = Mathf.Lerp(0.66f, 0.0f, Mathf.Clamp(value, 0f, 1f));
			mat.AlbedoColor = Color.FromHsv(hue, 0.8f, 0.9f);
		}
	}

	private static void ApplyOpacity(Node3D node, float value)
	{
		if (node is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D mat)
		{
			var color = mat.AlbedoColor;
			mat.AlbedoColor = new Color(color.R, color.G, color.B, Mathf.Clamp(value, 0f, 1f));
		}
	}

	internal static double Normalise(double value,
		double srcMin, double srcMax, double tgtMin, double tgtMax)
	{
		var range = srcMax - srcMin;
		if (range <= 0)
			return tgtMin;

		var clamped = Math.Clamp(value, srcMin, srcMax);
		var ratio = (clamped - srcMin) / range;
		return tgtMin + ratio * (tgtMax - tgtMin);
	}

	private static void ApplyTextMetric(ActiveBinding active,
		Godot.Collections.Dictionary metrics)
	{
		var metricKey = active.Resolved.Binding.Metric;
		if (!metrics.ContainsKey(metricKey))
			return;

		var metricData = metrics[metricKey].AsGodotDictionary();
		if (!metricData.ContainsKey("text_value"))
			return;

		active.TargetNode.Set("text", metricData["text_value"].AsString());
	}

	/// <summary>
	/// Test-only helper. Registers a text binding directly, bypassing
	/// BindFromSceneProperties scene traversal.
	/// </summary>
	internal void AddTextBindingForTest(string metricName, Node targetNode)
	{
		var fakeBinding = new MetricBinding(
			(string)targetNode.Name, metricName, "text",
			SourceRangeMin: 0, SourceRangeMax: 1,
			TargetRangeMin: 0, TargetRangeMax: 1,
			InstanceId: -1, InstanceName: null);
		var resolved = PropertyVocabulary.Resolve(fakeBinding);
		_activeBindings.Add(new ActiveBinding(resolved, targetNode));
	}
}
