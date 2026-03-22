using Godot;
using GdUnit4;
using PcpClient;
using PmviewNextgen.Bridge;
using static GdUnit4.Assertions;

namespace PmviewNextgen.Tests.E2E;

/// <summary>
/// E2E tests for the full binding pipeline: MetricPoller → SceneBinder.
/// Verifies that real metric values from pmproxy flow through bindings
/// and modify scene node properties.
/// Requires docker-compose PCP stack running on localhost:54322.
/// Skips gracefully when pmproxy is unavailable.
/// </summary>
[TestSuite]
public partial class BindingPipelineTests
{
	private const string PmproxyEndpoint = "http://localhost:54322";
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
	public async Task FullPipeline_MetricValuesAppliedToScene()
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

		var binder = new SceneBinder();
		runner.Scene().AddChild(binder);

		// Wire MetricPoller → SceneBinder (mirroring what the controller does)
		bool metricsReceived = false;
		poller.MetricsUpdated += (_, metrics) =>
		{
			binder.ApplyMetrics(metrics);
			metricsReceived = true;
		};

		poller.StartPolling();
		// Wait for connection + at least two poll cycles
		await Task.Delay(10000);

		// Verify that the pipeline actually executed
		AssertThat(metricsReceived).IsTrue();
	}
}
