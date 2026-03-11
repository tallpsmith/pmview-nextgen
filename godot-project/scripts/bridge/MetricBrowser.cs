using Godot;
using PcpClient;

namespace PmviewNextgen.Bridge;

/// <summary>
/// C# bridge node that wraps PcpClient discovery methods for GDScript.
/// Provides metric namespace traversal, description, and instance domain
/// enumeration as Godot signals and callable methods.
/// </summary>
public partial class MetricBrowser : Node
{
	[Signal]
	public delegate void ChildrenLoadedEventHandler(
		string prefix, string[] leaves, string[] nonLeaves);

	[Signal]
	public delegate void MetricDescribedEventHandler(
		Godot.Collections.Dictionary descriptor);

	[Signal]
	public delegate void InstanceDomainLoadedEventHandler(
		string metricName, Godot.Collections.Array instances);

	[Signal]
	public delegate void DiscoveryErrorEventHandler(string message);

	[Signal]
	public delegate void MetricSelectedEventHandler(
		string metricName, Godot.Collections.Dictionary descriptor);

	private PcpClientConnection? _client;

	/// <summary>
	/// Grabs the PcpClientConnection from a MetricPoller node.
	/// Called from GDScript with the MetricPoller node reference,
	/// avoiding the need to pass non-Godot types across the language boundary.
	/// </summary>
	public void ConnectToPoller(Node pollerNode)
	{
		if (pollerNode is MetricPoller poller)
		{
			_client = poller.Client;
		}
		else
		{
			GD.PushWarning("[MetricBrowser] Expected MetricPoller node");
		}
	}

	public async void BrowseChildren(string prefix)
	{
		if (_client == null || _client.State != ConnectionState.Connected)
		{
			EmitSignal(SignalName.DiscoveryError, "Not connected");
			return;
		}

		try
		{
			var ns = await _client.GetChildrenAsync(prefix);
			EmitSignal(SignalName.ChildrenLoaded,
				ns.Prefix,
				ns.LeafNames.ToArray(),
				ns.NonLeafNames.ToArray());
		}
		catch (PcpException ex)
		{
			EmitSignal(SignalName.DiscoveryError, ex.Message);
		}
	}

	public async void DescribeMetric(string metricName)
	{
		if (_client == null || _client.State != ConnectionState.Connected)
		{
			EmitSignal(SignalName.DiscoveryError, "Not connected");
			return;
		}

		try
		{
			var descriptors = await _client.DescribeMetricsAsync(new[] { metricName });
			if (descriptors.Count == 0)
			{
				EmitSignal(SignalName.DiscoveryError,
					$"No descriptor returned for {metricName}");
				return;
			}

			var desc = descriptors[0];
			var dict = MarshalDescriptor(desc);
			EmitSignal(SignalName.MetricDescribed, dict);
		}
		catch (PcpMetricNotFoundException)
		{
			EmitSignal(SignalName.DiscoveryError,
				$"Metric not found: {metricName}");
		}
		catch (PcpException ex)
		{
			EmitSignal(SignalName.DiscoveryError, ex.Message);
		}
	}

	public async void LoadInstanceDomain(string metricName)
	{
		if (_client == null || _client.State != ConnectionState.Connected)
		{
			EmitSignal(SignalName.DiscoveryError, "Not connected");
			return;
		}

		try
		{
			var indom = await _client.GetInstanceDomainAsync(metricName);
			var instances = new Godot.Collections.Array();
			foreach (var inst in indom.Instances)
			{
				instances.Add(new Godot.Collections.Dictionary
				{
					["id"] = inst.Id,
					["name"] = inst.Name
				});
			}

			EmitSignal(SignalName.InstanceDomainLoaded, metricName, instances);
		}
		catch (PcpException ex)
		{
			EmitSignal(SignalName.DiscoveryError, ex.Message);
		}
	}

	public async void SelectMetric(string metricName)
	{
		if (_client == null || _client.State != ConnectionState.Connected)
		{
			EmitSignal(SignalName.DiscoveryError, "Not connected");
			return;
		}

		try
		{
			var descriptors = await _client.DescribeMetricsAsync(new[] { metricName });
			if (descriptors.Count > 0)
			{
				var dict = MarshalDescriptor(descriptors[0]);
				EmitSignal(SignalName.MetricSelected, metricName, dict);
			}
		}
		catch (PcpException ex)
		{
			EmitSignal(SignalName.DiscoveryError, ex.Message);
		}
	}

	private static Godot.Collections.Dictionary MarshalDescriptor(MetricDescriptor desc)
	{
		return new Godot.Collections.Dictionary
		{
			["name"] = desc.Name,
			["pmid"] = desc.Pmid,
			["type"] = desc.Type.ToString(),
			["semantics"] = desc.Semantics.ToString(),
			["units"] = desc.Units ?? "",
			["indom_id"] = desc.IndomId ?? "",
			["one_line_help"] = desc.OneLineHelp,
			["long_help"] = desc.LongHelp ?? ""
		};
	}
}
