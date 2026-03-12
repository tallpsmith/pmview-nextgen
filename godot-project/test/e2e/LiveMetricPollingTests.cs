using Godot;
using GdUnit4;
using PcpClient;
using PmviewNextgen.Bridge;
using static GdUnit4.Assertions;

namespace PmviewNextgen.Tests.E2E;

/// <summary>
/// E2E tests for live metric polling against a real pmproxy instance.
/// Requires docker-compose PCP stack running on localhost:44322.
/// Skips gracefully when pmproxy is unavailable (no PCP stack in CI unit tests).
/// </summary>
[TestSuite]
public partial class LiveMetricPollingTests
{
	private const string PmproxyEndpoint = "http://localhost:44322";
	private static readonly System.Net.Http.HttpClient _probe = new()
	{
		Timeout = TimeSpan.FromSeconds(2)
	};

	private static bool IsPmproxyAvailable()
	{
		try
		{
			var response = _probe.GetAsync($"{PmproxyEndpoint}/pmapi/context")
				.GetAwaiter().GetResult();
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task ConnectsTopmproxy_EmitsConnected()
	{
		if (!IsPmproxyAvailable())
		{
			GD.Print("[E2E] Skipping: pmproxy not available at " + PmproxyEndpoint);
			return;
		}

		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var poller = new MetricPoller();
		poller.Endpoint = PmproxyEndpoint;
		poller.MetricNames = ["kernel.all.load"];
		runner.Scene().AddChild(poller);

		var states = new List<string>();
		poller.ConnectionStateChanged += state => states.Add(state);

		poller.StartPolling();
		await Task.Delay(5000);

		AssertThat(states).Contains("Connected");
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task FetchKernelAllLoad_EmitsMetricsUpdated()
	{
		if (!IsPmproxyAvailable())
		{
			GD.Print("[E2E] Skipping: pmproxy not available at " + PmproxyEndpoint);
			return;
		}

		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var poller = new MetricPoller();
		poller.Endpoint = PmproxyEndpoint;
		poller.PollIntervalMs = 500;
		poller.MetricNames = ["kernel.all.load"];
		runner.Scene().AddChild(poller);

		Godot.Collections.Dictionary? receivedMetrics = null;
		poller.MetricsUpdated += metrics => receivedMetrics = metrics;

		poller.StartPolling();
		await Task.Delay(8000);

		AssertThat(receivedMetrics).IsNotNull();
		AssertThat(receivedMetrics!.ContainsKey("kernel.all.load")).IsTrue();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task MetricsUpdated_HasExpectedStructure()
	{
		if (!IsPmproxyAvailable())
		{
			GD.Print("[E2E] Skipping: pmproxy not available at " + PmproxyEndpoint);
			return;
		}

		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var poller = new MetricPoller();
		poller.Endpoint = PmproxyEndpoint;
		poller.PollIntervalMs = 500;
		poller.MetricNames = ["kernel.all.load"];
		runner.Scene().AddChild(poller);

		Godot.Collections.Dictionary? receivedMetrics = null;
		poller.MetricsUpdated += metrics => receivedMetrics = metrics;

		poller.StartPolling();
		await Task.Delay(8000);

		AssertThat(receivedMetrics).IsNotNull();

		var metricData = receivedMetrics!["kernel.all.load"].AsGodotDictionary();
		AssertThat(metricData.ContainsKey("timestamp")).IsTrue();
		AssertThat(metricData.ContainsKey("instances")).IsTrue();
		AssertThat(metricData.ContainsKey("name_to_id")).IsTrue();

		var instances = metricData["instances"].AsGodotDictionary();
		AssertThat(instances.Count).IsGreater(0);
	}
}
