using Godot;
using PcpGodotBridge;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Godot Resource holding a single PCP metric binding configuration.
/// Serializes inline in .tscn files. Scene authors configure these via
/// the inspector on PcpBindable nodes.
/// </summary>
[Tool]
[GlobalClass]
public partial class PcpBindingResource : Resource
{
	[Export] public string MetricName { get; set; } = "";

	[Export(PropertyHint.Enum, "height,width,depth,scale,rotation_speed,position_y,color_temperature,opacity")]
	public string TargetProperty { get; set; } = "";

	[Export] public float SourceRangeMin { get; set; } = 0.0f;
	[Export] public float SourceRangeMax { get; set; } = 1.0f;
	[Export] public float TargetRangeMin { get; set; } = 0.0f;
	[Export] public float TargetRangeMax { get; set; } = 1.0f;
	[Export] public string InstanceName { get; set; } = "";
	[Export] public int InstanceId { get; set; } = -1;
	[Export] public float InitialValue { get; set; } = 0.0f;
	[Export] public string ZoneName { get; set; } = "";

	/// <summary>
	/// Converts this resource to a pure .NET MetricBinding for validation and runtime use.
	/// </summary>
	public MetricBinding ToMetricBinding(string nodeName)
	{
		return PcpBindingConverter.ToMetricBinding(
			nodeName, MetricName, TargetProperty,
			SourceRangeMin, SourceRangeMax,
			TargetRangeMin, TargetRangeMax,
			InstanceId, InstanceName,
			InitialValue,
			zoneName: string.IsNullOrWhiteSpace(ZoneName) ? null : ZoneName);
	}
}
