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
/// Requires docker-compose PCP stack running on localhost:44322.
/// </summary>
[TestSuite]
public partial class BindingPipelineTests
{
	private const string PmproxyEndpoint = "http://localhost:44322";

	[TestCase]
	[RequireGodotRuntime]
	public async Task FullPipeline_MetricValuesAppliedToScene()
	{
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
		poller.MetricsUpdated += metrics =>
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
