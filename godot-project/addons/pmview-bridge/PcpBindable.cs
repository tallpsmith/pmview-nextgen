using Godot;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Attach as a child of any Node3D to mark it as PCP-bindable.
/// Holds an array of PcpBindingResource entries configured via the inspector.
/// SceneBinder discovers these at runtime to wire up metric-to-property bindings.
/// </summary>
[Tool]
[GlobalClass]
public partial class PcpBindable : Node
{
	[Export]
	public Godot.Collections.Array<PcpBindingResource> PcpBindings { get; set; } = new();

	/// <summary>
	/// Returns distinct metric names from all configured bindings.
	/// </summary>
	public string[] GetMetricNames()
	{
		var names = new HashSet<string>();
		foreach (var binding in PcpBindings)
		{
			if (!string.IsNullOrEmpty(binding?.MetricName))
				names.Add(binding.MetricName);
		}
		return names.ToArray();
	}
}
